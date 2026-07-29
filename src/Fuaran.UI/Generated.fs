// AUTO-GENERATED from the IDL by Fuaran.Core.Idl.Gen (Phase 317 increment 3). Do not edit by hand.
module Fuaran.UI.Generated

open Fuaran.Core

[<RequireQualifiedAccess>]
type HeadingVariant =
    | Standard
    | Eyebrow
    | Caption
    | Lead

[<RequireQualifiedAccess>]
type BadgeVariant =
    | Neutral
    | Brand
    | Success
    | Warning
    | Critical
    | Info

[<RequireQualifiedAccess>]
type Orientation =
    | Vertical
    | Horizontal

[<RequireQualifiedAccess>]
type BoxRole =
    | Dashboard
    | Card
    | Group
    | Separator

[<RequireQualifiedAccess>]
type MathDisplay =
    | Inline
    | Block

[<RequireQualifiedAccess>]
type ImageVariant =
    | Default
    | Avatar
    | Rounded

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
type StyleWeight =
    | Compact
    | Standard
    | Spacious

[<RequireQualifiedAccess>]
type Emphasis =
    | Quiet
    | Normal
    | Loud

[<RequireQualifiedAccess>]
type ScrollOrientation =
    | Vertical
    | Horizontal
    | Both

[<RequireQualifiedAccess>]
type ButtonVariant =
    | Primary
    | Secondary
    | Tertiary
    | Destructive

[<RequireQualifiedAccess>]
type FileReadEncoding =
    | Text
    | Base64
    | DataUrl

[<RequireQualifiedAccess>]
type DateVariant =
    | Date
    | Time
    | DateTime

[<RequireQualifiedAccess>]
type DateStyle =
    | Short
    | Medium
    | Long
    | Full

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
type ChartKind =
    | Line
    | Bar
    | Area
    | Pie
    | Scatter
    | Heatmap

[<RequireQualifiedAccess>]
type HashStrictness =
    | StrictReplay
    | AdvisoryWarning
    | Enforced

[<RequireQualifiedAccess>]
type HostEffect =
    | Pure
    | ReadsHost
    | WritesHost

[<RequireQualifiedAccess>]
type DeterminismSource =
    | Deterministic
    | Clock
    | Random
    | Network

[<RequireQualifiedAccess>]
type ChannelDirection =
    | OutOnly
    | TwoWay

[<RequireQualifiedAccess>]
type TextAnchor =
    | Start
    | Middle
    | End

[<RequireQualifiedAccess>]
type StyleRole =
    | None
    | Eyebrow
    | Data
    | Lede
    | Caption

[<RequireQualifiedAccess>]
type FontVoice =
    | Default
    | Display
    | Structural

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

[<RequireQualifiedAccess>]
type TextSource =
    | Literal of text: string
    | Bound of binding: Binding<string>
    | I18n of key: string * args: Map<string, JVal>

and [<RequireQualifiedAccess>] Binding<'T> =
    | Static of value: 'T option
    | Query of name: string * accessor: (obj -> 'T) * dependsOn: string list option
    | Filter of name: string * defaultValue: 'T option
    | Selection of nodeId: string * accessor: (obj -> 'T) * defaultValue: 'T option * field: string option
    | State of key: string * defaultValue: 'T option
    | Computed of fn: (obj -> 'T)
    | Local of flushOn: LocalFlushTrigger * format: ('T -> string) * initialFrom: Binding<'T> * onCommit: ('T -> obj) option * parse: (string -> Result<'T, string>)
    | Format of source: Binding<float> * format: Format * locale: LocaleSource
    | I18n of key: string * args: Map<string, Binding<JVal>> option
    | Transform of source: Fuaran.Core.DataSource * pipeline: Fuaran.Core.Transform list * ``params``: TransformParam list option
    | Invoke of capabilityId: string * args: InvokeArg list

and [<RequireQualifiedAccess>] CellFormat =
    | None
    | Number of decimals: int option
    | Currency of code: string
    | Percent of decimals: int option
    | SignificantDigits of digits: int
    | Date of format: string
    | Custom of fn: (Fuaran.UI.HostPrelude.CellValue -> string)

and [<RequireQualifiedAccess>] Action<'Msg> =
    | Chain of ops: Action<'Msg> list
    | WriteToClipboard of text: string
    | Dispatch of msg: ('Msg)
    | Invoke of capabilityId: string * args: InvokeArg list
    | ReadFileBody of fileRef: string * fileHandle: (obj option) * encoding: FileReadEncoding * onRead: (string -> 'Msg) option
    | Call of endpoint: string * onResult: (obj -> 'Msg) option * into: CallResultTarget option
    | Navigate of route: string
    | CommitLocal of nodeId: string
    | Notify of channel: string * payload: JVal
    | SetState of key: string * value: JVal
    | AiTool of toolName: string * args: JVal

and [<RequireQualifiedAccess>] CallResultTarget =
    | State of key: string
    | Query of name: string

and [<RequireQualifiedAccess>] Format =
    | Number of decimals: int option
    | Currency of isoCode: string
    | Percent of decimals: int option
    | Date of dateStyle: DateStyle
    | RelativeTime of unit: RelativeTimeUnit

and [<RequireQualifiedAccess>] LocaleSource =
    | Ambient
    | Explicit of tag: string

and [<RequireQualifiedAccess>] LocalFlushTrigger =
    | OnBlur
    | OnSubmit
    | OnDebounce of milliseconds: int
    | OnCommitAction

and [<RequireQualifiedAccess>] LayoutMode =
    | Auto
    | Flex of direction: Orientation * wrap: bool * gap: int option
    | Grid of cols: int * templateColumns: string option * gap: int option

and [<RequireQualifiedAccess>] FormFieldKind<'Msg> =
    | Text of value: Binding<string> option * onChange: (string -> Action<'Msg>) option
    | Number of value: Binding<float> option * onChange: (float -> Action<'Msg>) option
    | Checkbox of value: Binding<bool> option * onToggle: (bool -> Action<'Msg>) option
    | Choice of options: Binding<SelectOption list> * value: Binding<string> option * onChange: (string option -> Action<'Msg>) option
    | TextArea of value: Binding<string> option * onChange: (string -> Action<'Msg>) option * rows: int
    | RangedNumber of value: Binding<float> option * onChange: (float -> Action<'Msg>) option * min: float option * max: float option * step: float option
    | Range of value: Binding<RangePair> option * onChange: (float * float -> Action<'Msg>) option * min: float option * max: float option * step: float option
    | SegmentedChoice of options: Binding<SelectOption list> * value: Binding<string> option * onChange: (string option -> Action<'Msg>) option * orientation: Orientation
    | Date of value: Binding<string> option * onChange: (string option -> Action<'Msg>) option * variant: DateVariant * min: string option * max: string option * step: float option
    | DateRange of value: Binding<DateRangePair> option * onChange: (string * string -> Action<'Msg>) option * variant: DateVariant * min: string option * max: string option * step: float option

and [<RequireQualifiedAccess>] ColumnWidth =
    | Auto
    | Fixed of pixels: int
    | Flex of weight: float

and [<RequireQualifiedAccess>] CellKindErased<'Msg> =
    | Text
    | Numeric
    | Date
    | Editable of onEdit: (obj * Fuaran.UI.HostPrelude.CellValue -> Action<'Msg>) option
    | Checkbox of get: (obj -> bool) * onToggle: (obj * bool -> Action<'Msg>) option
    | Button of label: TextSource * onClick: (obj -> Action<'Msg>) option
    | ButtonGroup of buttons: ButtonGroupItem<'Msg> list
    | Link of hrefFn: (obj -> string) * labelFn: (obj -> TextSource)
    | Pill of labelFn: (obj -> TextSource) * toneFn: (obj -> ToneVariant)
    | Progress of fractionFn: (obj -> float) * labelFn: (obj -> TextSource) option
    | Custom of fn: ((obj -> JVal) -> Node<'Msg>)

and [<RequireQualifiedAccess>] HoleValueSpace =
    | IntRange of min: int * max: int
    | FloatRange of min: float * max: float
    | StringLen of minLen: int * maxLen: int
    | Enum of choices: string list
    | AnyString

and [<RequireQualifiedAccess>] Scalar =
    | Int of value: int
    | Float of value: float
    | Bool of value: bool
    | Str of value: string

and [<RequireQualifiedAccess>] HoleDecl =
    | Value of name: string * space: HoleValueSpace * ``default``: Scalar option
    | Slot of name: string * kindConstraint: string option
    | Repeat of name: string * countSpace: HoleValueSpace

and [<RequireQualifiedAccess>] FragmentArg<'Msg> =
    | Int of value: int
    | Float of value: float
    | Bool of value: bool
    | Str of value: string
    | SlotArg of tree: Node<'Msg>

and [<RequireQualifiedAccess>] CurveCommand =
    | MoveTo of ``to``: DrawPoint
    | LineTo of ``to``: DrawPoint
    | CubicTo of control1: DrawPoint * control2: DrawPoint * ``to``: DrawPoint
    | QuadraticTo of control: DrawPoint * ``to``: DrawPoint
    | Close

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

and SemanticStyle =
    {
      Emphasis: Emphasis
      Role: StyleRole
      Tone: ToneVariant
      Voice: FontVoice
      Weight: StyleWeight
    }

and StateBehaviour<'Msg> =
    {
      OnEmpty: Node<'Msg> option
      OnError: (Fuaran.UI.HostPrelude.ErrorPayload -> Node<'Msg>) option
      OnLoading: Node<'Msg> option
    }

and Accessibility =
    {
      DescribedBy: string option
      Hidden: Binding<bool> option
      Label: Binding<string> option
      LabelledBy: string option
      LiveRegion: Fuaran.UI.HostPrelude.LiveRegionKind option
      Role: Fuaran.UI.HostPrelude.AriaRole option
    }

and SwitchCase<'Msg> =
    {
      Child: Node<'Msg>
      Match: string
    }

and GuestChannel =
    {
      Direction: ChannelDirection
      MessageShape: string option
    }

and DrawPoint =
    {
      X: float
      Y: float
    }

and ViewBox =
    {
      Height: float
      MinX: float
      MinY: float
      Width: float
    }

and DrawStyle =
    {
      Emphasis: Emphasis option
      Fill: Binding<string> option
      FontFamily: string option
      FontSize: float option
      MarkId: string option
      Opacity: Binding<float> option
      Stroke: Binding<string> option
      StrokeWidth: Binding<float> option
      TextAnchor: TextAnchor option
    }

and InvokeArg =
    {
      Addr: string
      Value: string
    }

and SelectOption =
    {
      Label: string
      Value: string
    }

and MapMarker =
    {
      Label: string
      Latitude: float
      Longitude: float
    }

and StaticRows =
    {
      Headers: TextSource list
      Rows: TextSource list list
    }

and FormField<'Msg> =
    {
      Id: string
      Kind: FormFieldKind<'Msg>
      Label: TextSource
      Required: bool
      Help: TextSource option
    }

and FilterSpec<'Msg> =
    {
      Kind: FormFieldKind<'Msg>
      Label: TextSource
      Name: string
    }

and TransformParam =
    {
      From: Binding<JVal>
      Name: string
    }

and RangePair =
    {
      Max: float
      Min: float
    }

and DateRangePair =
    {
      From: string
      To: string
    }

and TabHeader =
    {
      Label: TextSource
      Icon: string option
      Disabled: Binding<bool> option
    }

and ColumnErased<'Msg> =
    {
      Field: string option
      Format: CellFormat
      Kind: CellKindErased<'Msg>
      Label: string
      Value: (obj -> Fuaran.UI.HostPrelude.CellValue) option
      Width: ColumnWidth
    }

and ButtonGroupItem<'Msg> =
    {
      Label: TextSource
      OnClick: (obj -> Action<'Msg>) option
    }

and ContentHash =
    {
      Algorithm: string
      Hash: string
      Strictness: HashStrictness
    }

and EffectClass =
    {
      Determinism: DeterminismSource
      HostEffect: HostEffect
    }

// Display
and HeadingSpec =
    {
      Level: int
      Text: TextSource
      Variant: HeadingVariant
    }

// Display
and BadgeSpec =
    {
      Label: TextSource
      Variant: BadgeVariant
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
and SkeletonSpec =
    {
      Rows: int
    }

// Display
and ListSpec =
    {
      Items: TextSource list
      Ordered: bool
    }

// Display
and ImageSpec =
    {
      Alt: TextSource
      Src: Binding<string>
      Variant: ImageVariant
    }

// Display
and LinkSpec =
    {
      Href: Binding<string>
      Label: TextSource
      Download: bool
      Rel: string option
      Target: string option
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

// Display
and ProgressSpec =
    {
      Fraction: Binding<float>
      Indeterminate: bool
      Tone: ToneVariant
      Label: TextSource option
      Caveat: TextSource option
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
      Icon: string option
      Subtext: TextSource option
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
and FactSpec =
    {
      Emphasis: bool
      Help: TextSource option
      Icon: string option
      Label: TextSource
      Tone: ToneVariant
      Value: TextSource
    }

// Display
and SparklineSpec =
    {
      Source: Binding<float list>
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

// Display
and ToastSpec =
    {
      Dismissable: bool
      Message: TextSource
      Open: Binding<bool>
      Tone: ToneVariant
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

// Layout
and BoxSpec<'Msg> =
    {
      Children: Node<'Msg> list
      Heading: TextSource option
      Layout: LayoutMode
      Role: BoxRole
    }

// Layout
and SplitPanelSpec<'Msg> =
    {
      Children: Node<'Msg> list
      Weight: float
    }

// Layout
and SummaryListSpec<'Msg> =
    {
      Children: Node<'Msg> list
      Heading: TextSource option
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

// Layout
and ModalSpec<'Msg> =
    {
      Children: Node<'Msg> list
      Dismissable: bool
      OnDismiss: Action<'Msg> option
      Open: Binding<bool>
      Heading: TextSource option
    }

// Layout
and ScrollAreaSpec<'Msg> =
    {
      Children: Node<'Msg> list
      Orientation: ScrollOrientation
      MaxHeight: int option
      MaxWidth: int option
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

// Layout
and StepperSpec<'Msg> =
    {
      ActiveStep: Binding<int>
      Children: Node<'Msg> list
      OnSelect: (int -> Action<'Msg>) option
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

// Input
and FileUploadSpec<'Msg> =
    {
      Accept: string list
      Label: TextSource
      Multiple: bool
      OnSelect: (Fuaran.UI.HostPrelude.FileSelection list -> Action<'Msg>) option
      Disabled: Binding<bool> option
    }

// Input
and FormSpec<'Msg> =
    {
      Fields: FormField<'Msg> list
      OnSubmit: Action<'Msg>
      SubmitLabel: TextSource
      Disabled: Binding<bool> option
    }

// Input
and FiltersSpec<'Msg> =
    {
      Items: FilterSpec<'Msg> list
    }

// Visualisation
and DataGridSpec<'Msg> =
    {
      Columns: ColumnErased<'Msg> list
      Editable: bool
      RowKey: (obj -> string) option
      RowKeyField: string option
      Source: Binding<obj seq>
      StaticRows: StaticRows option
      OnRowClick: (obj -> Action<'Msg>) option
    }

// Visualisation
and ChartSpec<'Msg> =
    {
      Kind: ChartKind
      Source: Binding<obj seq>
      Stacked: bool
      XField: string
      YFields: string list
      Title: TextSource option
      OnPointClick: (obj -> Action<'Msg>) option
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

// Meta
and CustomSpec =
    {
      ModuleId: string
      ComponentId: string
      Props: Map<string, JVal>
      ContentHash: ContentHash option
      ExposedNodeIds: string list option
    }

// Meta
and ErrorBoundarySpec<'Msg> =
    {
      Child: Node<'Msg>
      Fallback: Node<'Msg>
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

// Meta
and SwitchSpec<'Msg> =
    {
      Cases: SwitchCase<'Msg> list
      Default: Node<'Msg>
      StateKey: string
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

and [<RequireQualifiedAccess>] NodeKind<'Msg> =
    | Heading of HeadingSpec
    | Badge of BadgeSpec
    | Markdown of MarkdownSpec
    | Math of MathSpec
    | Skeleton of SkeletonSpec
    | List of ListSpec
    | Image of ImageSpec
    | Link of LinkSpec
    | Callout of CalloutSpec
    | Progress of ProgressSpec
    | Metric of MetricSpec
    | LabelValueRow of LabelValueRowSpec
    | Fact of FactSpec
    | Sparkline of SparklineSpec
    | CodeBlock of CodeBlockSpec
    | Toast of ToastSpec
    | Drawing of DrawingSpec
    | Box of BoxSpec<'Msg>
    | SplitPanel of SplitPanelSpec<'Msg>
    | SummaryList of SummaryListSpec<'Msg>
    | Disclosure of DisclosureSpec<'Msg>
    | Modal of ModalSpec<'Msg>
    | ScrollArea of ScrollAreaSpec<'Msg>
    | Tabs of TabsSpec<'Msg>
    | Stepper of StepperSpec<'Msg>
    | Button of ButtonSpec<'Msg>
    | Select of SelectSpec<'Msg>
    | FileUpload of FileUploadSpec<'Msg>
    | Form of FormSpec<'Msg>
    | Filters of FiltersSpec<'Msg>
    | DataGrid of DataGridSpec<'Msg>
    | Chart of ChartSpec<'Msg>
    | Map of MapSpec<'Msg>
    | Custom of CustomSpec
    | ErrorBoundary of ErrorBoundarySpec<'Msg>
    | FragmentDecl of FragmentDeclSpec<'Msg>
    | FragmentRef of FragmentRefSpec<'Msg>
    | Switch of SwitchSpec<'Msg>
    | Mount of MountSpec<'Msg>

and Node<'Msg> =
    {
      Id: string
      Kind: NodeKind<'Msg>
      Accessibility: Accessibility option
      ExtraAttributes: (Map<string, string> option)
      Motion: (Motion option)
      State: StateBehaviour<'Msg> option
      Style: SemanticStyle option
    }

let private encHeadingVariant (v: HeadingVariant) : JVal =
    match v with
    | HeadingVariant.Standard -> JStr "Standard"
    | HeadingVariant.Eyebrow -> JStr "Eyebrow"
    | HeadingVariant.Caption -> JStr "Caption"
    | HeadingVariant.Lead -> JStr "Lead"

let private encBadgeVariant (v: BadgeVariant) : JVal =
    match v with
    | BadgeVariant.Neutral -> JStr "Neutral"
    | BadgeVariant.Brand -> JStr "Brand"
    | BadgeVariant.Success -> JStr "Success"
    | BadgeVariant.Warning -> JStr "Warning"
    | BadgeVariant.Critical -> JStr "Critical"
    | BadgeVariant.Info -> JStr "Info"

let private encOrientation (v: Orientation) : JVal =
    match v with
    | Orientation.Vertical -> JStr "Vertical"
    | Orientation.Horizontal -> JStr "Horizontal"

let private encBoxRole (v: BoxRole) : JVal =
    match v with
    | BoxRole.Dashboard -> JStr "Dashboard"
    | BoxRole.Card -> JStr "Card"
    | BoxRole.Group -> JStr "Group"
    | BoxRole.Separator -> JStr "Separator"

let private encMathDisplay (v: MathDisplay) : JVal =
    match v with
    | MathDisplay.Inline -> JStr "Inline"
    | MathDisplay.Block -> JStr "Block"

let private encImageVariant (v: ImageVariant) : JVal =
    match v with
    | ImageVariant.Default -> JStr "Default"
    | ImageVariant.Avatar -> JStr "Avatar"
    | ImageVariant.Rounded -> JStr "Rounded"

let private encToneVariant (v: ToneVariant) : JVal =
    match v with
    | ToneVariant.Default -> JStr "Default"
    | ToneVariant.Subdued -> JStr "Subdued"
    | ToneVariant.Brand -> JStr "Brand"
    | ToneVariant.Success -> JStr "Success"
    | ToneVariant.Warning -> JStr "Warning"
    | ToneVariant.Critical -> JStr "Critical"
    | ToneVariant.Info -> JStr "Info"

let private encStyleWeight (v: StyleWeight) : JVal =
    match v with
    | StyleWeight.Compact -> JStr "Compact"
    | StyleWeight.Standard -> JStr "Standard"
    | StyleWeight.Spacious -> JStr "Spacious"

let private encEmphasis (v: Emphasis) : JVal =
    match v with
    | Emphasis.Quiet -> JStr "Quiet"
    | Emphasis.Normal -> JStr "Normal"
    | Emphasis.Loud -> JStr "Loud"

let private encScrollOrientation (v: ScrollOrientation) : JVal =
    match v with
    | ScrollOrientation.Vertical -> JStr "Vertical"
    | ScrollOrientation.Horizontal -> JStr "Horizontal"
    | ScrollOrientation.Both -> JStr "Both"

let private encButtonVariant (v: ButtonVariant) : JVal =
    match v with
    | ButtonVariant.Primary -> JStr "Primary"
    | ButtonVariant.Secondary -> JStr "Secondary"
    | ButtonVariant.Tertiary -> JStr "Tertiary"
    | ButtonVariant.Destructive -> JStr "Destructive"

let private encFileReadEncoding (v: FileReadEncoding) : JVal =
    match v with
    | FileReadEncoding.Text -> JStr "Text"
    | FileReadEncoding.Base64 -> JStr "Base64"
    | FileReadEncoding.DataUrl -> JStr "DataUrl"

let private encDateVariant (v: DateVariant) : JVal =
    match v with
    | DateVariant.Date -> JStr "Date"
    | DateVariant.Time -> JStr "Time"
    | DateVariant.DateTime -> JStr "DateTime"

let private encDateStyle (v: DateStyle) : JVal =
    match v with
    | DateStyle.Short -> JStr "Short"
    | DateStyle.Medium -> JStr "Medium"
    | DateStyle.Long -> JStr "Long"
    | DateStyle.Full -> JStr "Full"

let private encRelativeTimeUnit (v: RelativeTimeUnit) : JVal =
    match v with
    | RelativeTimeUnit.Second -> JStr "Second"
    | RelativeTimeUnit.Minute -> JStr "Minute"
    | RelativeTimeUnit.Hour -> JStr "Hour"
    | RelativeTimeUnit.Day -> JStr "Day"
    | RelativeTimeUnit.Week -> JStr "Week"
    | RelativeTimeUnit.Month -> JStr "Month"
    | RelativeTimeUnit.Year -> JStr "Year"

let private encChartKind (v: ChartKind) : JVal =
    match v with
    | ChartKind.Line -> JStr "Line"
    | ChartKind.Bar -> JStr "Bar"
    | ChartKind.Area -> JStr "Area"
    | ChartKind.Pie -> JStr "Pie"
    | ChartKind.Scatter -> JStr "Scatter"
    | ChartKind.Heatmap -> JStr "Heatmap"

let private encHashStrictness (v: HashStrictness) : JVal =
    match v with
    | HashStrictness.StrictReplay -> JStr "StrictReplay"
    | HashStrictness.AdvisoryWarning -> JStr "AdvisoryWarning"
    | HashStrictness.Enforced -> JStr "Enforced"

let private encHostEffect (v: HostEffect) : JVal =
    match v with
    | HostEffect.Pure -> JStr "Pure"
    | HostEffect.ReadsHost -> JStr "ReadsHost"
    | HostEffect.WritesHost -> JStr "WritesHost"

let private encDeterminismSource (v: DeterminismSource) : JVal =
    match v with
    | DeterminismSource.Deterministic -> JStr "Deterministic"
    | DeterminismSource.Clock -> JStr "Clock"
    | DeterminismSource.Random -> JStr "Random"
    | DeterminismSource.Network -> JStr "Network"

let private encChannelDirection (v: ChannelDirection) : JVal =
    match v with
    | ChannelDirection.OutOnly -> JStr "OutOnly"
    | ChannelDirection.TwoWay -> JStr "TwoWay"

let private encTextAnchor (v: TextAnchor) : JVal =
    match v with
    | TextAnchor.Start -> JStr "Start"
    | TextAnchor.Middle -> JStr "Middle"
    | TextAnchor.End -> JStr "End"

let private encStyleRole (v: StyleRole) : JVal =
    match v with
    | StyleRole.None -> JStr "None"
    | StyleRole.Eyebrow -> JStr "Eyebrow"
    | StyleRole.Data -> JStr "Data"
    | StyleRole.Lede -> JStr "Lede"
    | StyleRole.Caption -> JStr "Caption"

let private encFontVoice (v: FontVoice) : JVal =
    match v with
    | FontVoice.Default -> JStr "Default"
    | FontVoice.Display -> JStr "Display"
    | FontVoice.Structural -> JStr "Structural"

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

let rec private encNodeKind (k: NodeKind<'Msg>) : JVal =
    match k with
    | NodeKind.Heading s -> encHeadingSpec s
    | NodeKind.Badge s -> encBadgeSpec s
    | NodeKind.Markdown s -> encMarkdownSpec s
    | NodeKind.Math s -> encMathSpec s
    | NodeKind.Skeleton s -> encSkeletonSpec s
    | NodeKind.List s -> encListSpec s
    | NodeKind.Image s -> encImageSpec s
    | NodeKind.Link s -> encLinkSpec s
    | NodeKind.Callout s -> encCalloutSpec s
    | NodeKind.Progress s -> encProgressSpec s
    | NodeKind.Metric s -> encMetricSpec s
    | NodeKind.LabelValueRow s -> encLabelValueRowSpec s
    | NodeKind.Fact s -> encFactSpec s
    | NodeKind.Sparkline s -> encSparklineSpec s
    | NodeKind.CodeBlock s -> encCodeBlockSpec s
    | NodeKind.Toast s -> encToastSpec s
    | NodeKind.Drawing s -> encDrawingSpec s
    | NodeKind.Box s -> encBoxSpec s
    | NodeKind.SplitPanel s -> encSplitPanelSpec s
    | NodeKind.SummaryList s -> encSummaryListSpec s
    | NodeKind.Disclosure s -> encDisclosureSpec s
    | NodeKind.Modal s -> encModalSpec s
    | NodeKind.ScrollArea s -> encScrollAreaSpec s
    | NodeKind.Tabs s -> encTabsSpec s
    | NodeKind.Stepper s -> encStepperSpec s
    | NodeKind.Button s -> encButtonSpec s
    | NodeKind.Select s -> encSelectSpec s
    | NodeKind.FileUpload s -> encFileUploadSpec s
    | NodeKind.Form s -> encFormSpec s
    | NodeKind.Filters s -> encFiltersSpec s
    | NodeKind.DataGrid s -> encDataGridSpec s
    | NodeKind.Chart s -> encChartSpec s
    | NodeKind.Map s -> encMapSpec s
    | NodeKind.Custom s -> encCustomSpec s
    | NodeKind.ErrorBoundary s -> encErrorBoundarySpec s
    | NodeKind.FragmentDecl s -> encFragmentDeclSpec s
    | NodeKind.FragmentRef s -> encFragmentRefSpec s
    | NodeKind.Switch s -> encSwitchSpec s
    | NodeKind.Mount s -> encMountSpec s

and private encNode (n: Node<'Msg>) : JVal =
    let kind = encNodeKind n.Kind

    JObj([ Some("id", JStr n.Id); Some("kind", kind); (n.Accessibility |> Option.map (fun v -> "accessibility", encAccessibility v)); None; None; (n.State |> Option.map (fun v -> "state", encStateBehaviour v)); (n.Style |> Option.map (fun v -> "style", encSemanticStyle v)) ] |> List.choose id)

and private encTextSource (v: TextSource) : JVal =
    match v with
    | TextSource.Literal text -> JStr text
    | TextSource.Bound binding -> Canon.typed "Bound" [ "binding", (encBinding JStr) binding ]
    | TextSource.I18n (key, args) -> Canon.typed "I18n" [ "key", JStr key; "args", (fun __m -> JObj(Map.toList __m |> List.map (fun (k, v) -> k, id v))) args ]

and private encBinding<'T> (encT: 'T -> JVal) (v: Binding<'T>) : JVal =
    match v with
    | Binding.Static value -> Canon.typed "Static" ([ (value |> Option.map (fun v -> "value", encT v)) ] |> List.choose id)
    | Binding.Query (name, accessor, dependsOn) -> Canon.typed "Query" ([ Some("name", JStr name); None; (dependsOn |> Option.map (fun v -> "dependsOn", JArr(List.map JStr v))) ] |> List.choose id)
    | Binding.Filter (name, defaultValue) -> Canon.typed "Filter" ([ Some("name", JStr name); (defaultValue |> Option.map (fun v -> "defaultValue", encT v)) ] |> List.choose id)
    | Binding.Selection (nodeId, accessor, defaultValue, field) -> Canon.typed "Selection" ([ Some("nodeId", JStr nodeId); None; (defaultValue |> Option.map (fun v -> "defaultValue", encT v)); (field |> Option.map (fun v -> "field", JStr v)) ] |> List.choose id)
    | Binding.State (key, defaultValue) -> Canon.typed "State" ([ Some("key", JStr key); (defaultValue |> Option.map (fun v -> "defaultValue", encT v)) ] |> List.choose id)
    | Binding.Computed fn -> Canon.typed "Computed" [ "fn", JStr "<closure>" ]
    | Binding.Local (flushOn, format, initialFrom, onCommit, parse) -> Canon.typed "Local" ([ Some("flushOn", encLocalFlushTrigger flushOn); Some("format", JStr "<closure>"); Some("initialFrom", (encBinding encT) initialFrom); (onCommit |> Option.map (fun v -> "onCommit", JStr "<closure>")); Some("parse", JStr "<closure>") ] |> List.choose id)
    | Binding.Format (source, format, locale) -> Canon.typed "Format" [ "source", (encBinding JFloat) source; "format", encFormat format; "locale", encLocaleSource locale ]
    | Binding.I18n (key, args) -> Canon.typed "I18n" ([ Some("key", JStr key); (args |> Option.map (fun v -> "args", (fun __m -> JObj(Map.toList __m |> List.map (fun (k, v) -> k, (encBinding id) v))) v)) ] |> List.choose id)
    | Binding.Transform (source, pipeline, ``params``) -> Canon.typed "Transform" ([ Some("source", Fuaran.Core.ColumnCodec.encodeJson source); Some("pipeline", JArr(List.map Fuaran.Core.DataFrameCodec.encodeTransform pipeline)); (``params`` |> Option.map (fun v -> "params", JArr(List.map encTransformParam v))) ] |> List.choose id)
    | Binding.Invoke (capabilityId, args) -> Canon.typed "Invoke" [ "capabilityId", JStr capabilityId; "args", JArr(List.map encInvokeArg args) ]

and private encCellFormat (v: CellFormat) : JVal =
    match v with
    | CellFormat.None -> Canon.typed "None" [  ]
    | CellFormat.Number decimals -> Canon.typed "Number" ([ (decimals |> Option.map (fun v -> "decimals", JInt v)) ] |> List.choose id)
    | CellFormat.Currency code -> Canon.typed "Currency" [ "code", JStr code ]
    | CellFormat.Percent decimals -> Canon.typed "Percent" ([ (decimals |> Option.map (fun v -> "decimals", JInt v)) ] |> List.choose id)
    | CellFormat.SignificantDigits digits -> Canon.typed "SignificantDigits" [ "digits", JInt digits ]
    | CellFormat.Date format -> Canon.typed "Date" [ "format", JStr format ]
    | CellFormat.Custom fn -> Canon.typed "Custom" [ "fn", JStr "<closure>" ]

and private encAction<'Msg> (v: Action<'Msg>) : JVal =
    match v with
    | Action.Chain ops -> Canon.typed "Chain" [ "ops", JArr(List.map encAction ops) ]
    | Action.WriteToClipboard text -> Canon.typed "WriteToClipboard" [ "text", JStr text ]
    | Action.Dispatch msg -> Canon.typed "Dispatch" ([ None ] |> List.choose id)
    | Action.Invoke (capabilityId, args) -> Canon.typed "Invoke" [ "capabilityId", JStr capabilityId; "args", JArr(List.map encInvokeArg args) ]
    | Action.ReadFileBody (fileRef, fileHandle, encoding, onRead) -> Canon.typed "ReadFileBody" ([ Some("fileRef", JStr fileRef); None; Some("encoding", encFileReadEncoding encoding); (onRead |> Option.map (fun v -> "onRead", JStr "<closure>")) ] |> List.choose id)
    | Action.Call (endpoint, onResult, into) -> Canon.typed "Call" ([ Some("endpoint", JStr endpoint); (onResult |> Option.map (fun v -> "onResult", JStr "<closure>")); (into |> Option.map (fun v -> "into", encCallResultTarget v)) ] |> List.choose id)
    | Action.Navigate route -> Canon.typed "Navigate" [ "route", JStr route ]
    | Action.CommitLocal nodeId -> Canon.typed "CommitLocal" [ "nodeId", JStr nodeId ]
    | Action.Notify (channel, payload) -> Canon.typed "Notify" [ "channel", JStr channel; "payload", id payload ]
    | Action.SetState (key, value) -> Canon.typed "SetState" [ "key", JStr key; "value", id value ]
    | Action.AiTool (toolName, args) -> Canon.typed "AiTool" [ "toolName", JStr toolName; "args", id args ]

and private encCallResultTarget (v: CallResultTarget) : JVal =
    match v with
    | CallResultTarget.State key -> Canon.typed "State" [ "key", JStr key ]
    | CallResultTarget.Query name -> Canon.typed "Query" [ "name", JStr name ]

and private encFormat (v: Format) : JVal =
    match v with
    | Format.Number decimals -> Canon.typed "Number" ([ (decimals |> Option.map (fun v -> "decimals", JInt v)) ] |> List.choose id)
    | Format.Currency isoCode -> Canon.typed "Currency" [ "isoCode", JStr isoCode ]
    | Format.Percent decimals -> Canon.typed "Percent" ([ (decimals |> Option.map (fun v -> "decimals", JInt v)) ] |> List.choose id)
    | Format.Date dateStyle -> Canon.typed "Date" [ "dateStyle", encDateStyle dateStyle ]
    | Format.RelativeTime unit -> Canon.typed "RelativeTime" [ "unit", encRelativeTimeUnit unit ]

and private encLocaleSource (v: LocaleSource) : JVal =
    match v with
    | LocaleSource.Ambient -> Canon.typed "Ambient" [  ]
    | LocaleSource.Explicit tag -> Canon.typed "Explicit" [ "tag", JStr tag ]

and private encLocalFlushTrigger (v: LocalFlushTrigger) : JVal =
    match v with
    | LocalFlushTrigger.OnBlur -> Canon.typed "OnBlur" [  ]
    | LocalFlushTrigger.OnSubmit -> Canon.typed "OnSubmit" [  ]
    | LocalFlushTrigger.OnDebounce milliseconds -> Canon.typed "OnDebounce" [ "milliseconds", JInt milliseconds ]
    | LocalFlushTrigger.OnCommitAction -> Canon.typed "OnCommitAction" [  ]

and private encLayoutMode (v: LayoutMode) : JVal =
    match v with
    | LayoutMode.Auto -> Canon.typed "Auto" [  ]
    | LayoutMode.Flex (direction, wrap, gap) -> Canon.typed "Flex" ([ Some("direction", encOrientation direction); Some("wrap", JBool wrap); (gap |> Option.map (fun v -> "gap", JInt v)) ] |> List.choose id)
    | LayoutMode.Grid (cols, templateColumns, gap) -> Canon.typed "Grid" ([ Some("cols", JInt cols); (templateColumns |> Option.map (fun v -> "templateColumns", JStr v)); (gap |> Option.map (fun v -> "gap", JInt v)) ] |> List.choose id)

and private encFormFieldKind<'Msg> (v: FormFieldKind<'Msg>) : JVal =
    match v with
    | FormFieldKind.Text (value, onChange) -> Canon.typed "Text" ([ (value |> Option.map (fun v -> "value", (encBinding JStr) v)); (onChange |> Option.map (fun v -> "onChange", JStr "<closure>")) ] |> List.choose id)
    | FormFieldKind.Number (value, onChange) -> Canon.typed "Number" ([ (value |> Option.map (fun v -> "value", (encBinding JFloat) v)); (onChange |> Option.map (fun v -> "onChange", JStr "<closure>")) ] |> List.choose id)
    | FormFieldKind.Checkbox (value, onToggle) -> Canon.typed "Checkbox" ([ (value |> Option.map (fun v -> "value", (encBinding JBool) v)); (onToggle |> Option.map (fun v -> "onToggle", JStr "<closure>")) ] |> List.choose id)
    | FormFieldKind.Choice (options, value, onChange) -> Canon.typed "Choice" ([ Some("options", (encBinding (fun __xs -> JArr(List.map encSelectOption __xs))) options); (value |> Option.map (fun v -> "value", (encBinding JStr) v)); (onChange |> Option.map (fun v -> "onChange", JStr "<closure>")) ] |> List.choose id)
    | FormFieldKind.TextArea (value, onChange, rows) -> Canon.typed "TextArea" ([ (value |> Option.map (fun v -> "value", (encBinding JStr) v)); (onChange |> Option.map (fun v -> "onChange", JStr "<closure>")); Some("rows", JInt rows) ] |> List.choose id)
    | FormFieldKind.RangedNumber (value, onChange, min, max, step) -> Canon.typed "RangedNumber" ([ (value |> Option.map (fun v -> "value", (encBinding JFloat) v)); (onChange |> Option.map (fun v -> "onChange", JStr "<closure>")); (min |> Option.map (fun v -> "min", JFloat v)); (max |> Option.map (fun v -> "max", JFloat v)); (step |> Option.map (fun v -> "step", JFloat v)) ] |> List.choose id)
    | FormFieldKind.Range (value, onChange, min, max, step) -> Canon.typed "Range" ([ (value |> Option.map (fun v -> "value", (fun (v: Binding<RangePair>) -> match v with | Binding.Static(Some p) -> encRangePair p | __other -> encBinding encRangePair __other) v)); (onChange |> Option.map (fun v -> "onChange", JStr "<closure>")); (min |> Option.map (fun v -> "min", JFloat v)); (max |> Option.map (fun v -> "max", JFloat v)); (step |> Option.map (fun v -> "step", JFloat v)) ] |> List.choose id)
    | FormFieldKind.SegmentedChoice (options, value, onChange, orientation) -> Canon.typed "SegmentedChoice" ([ Some("options", (encBinding (fun __xs -> JArr(List.map encSelectOption __xs))) options); (value |> Option.map (fun v -> "value", (encBinding JStr) v)); (onChange |> Option.map (fun v -> "onChange", JStr "<closure>")); Some("orientation", encOrientation orientation) ] |> List.choose id)
    | FormFieldKind.Date (value, onChange, variant, min, max, step) -> Canon.typed "Date" ([ (value |> Option.map (fun v -> "value", (encBinding JStr) v)); (onChange |> Option.map (fun v -> "onChange", JStr "<closure>")); Some("variant", encDateVariant variant); (min |> Option.map (fun v -> "min", JStr v)); (max |> Option.map (fun v -> "max", JStr v)); (step |> Option.map (fun v -> "step", JFloat v)) ] |> List.choose id)
    | FormFieldKind.DateRange (value, onChange, variant, min, max, step) -> Canon.typed "DateRange" ([ (value |> Option.map (fun v -> "value", (fun (v: Binding<DateRangePair>) -> match v with | Binding.Static(Some p) -> encDateRangePair p | __other -> encBinding encDateRangePair __other) v)); (onChange |> Option.map (fun v -> "onChange", JStr "<closure>")); Some("variant", encDateVariant variant); (min |> Option.map (fun v -> "min", JStr v)); (max |> Option.map (fun v -> "max", JStr v)); (step |> Option.map (fun v -> "step", JFloat v)) ] |> List.choose id)

and private encColumnWidth (v: ColumnWidth) : JVal =
    match v with
    | ColumnWidth.Auto -> Canon.typed "Auto" [  ]
    | ColumnWidth.Fixed pixels -> Canon.typed "Fixed" [ "pixels", JInt pixels ]
    | ColumnWidth.Flex weight -> Canon.typed "Flex" [ "weight", JFloat weight ]

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
    | CellKindErased.Progress (fractionFn, labelFn) -> Canon.typed "Progress" ([ Some("fractionFn", JStr "<closure>"); (labelFn |> Option.map (fun v -> "labelFn", JStr "<closure>")) ] |> List.choose id)
    | CellKindErased.Custom fn -> Canon.typed "Custom" [ "fn", JStr "<closure>" ]

and private encHoleValueSpace (v: HoleValueSpace) : JVal =
    match v with
    | HoleValueSpace.IntRange (min, max) -> Canon.typed "IntRange" [ "min", JInt min; "max", JInt max ]
    | HoleValueSpace.FloatRange (min, max) -> Canon.typed "FloatRange" [ "min", JFloat min; "max", JFloat max ]
    | HoleValueSpace.StringLen (minLen, maxLen) -> Canon.typed "StringLen" [ "minLen", JInt minLen; "maxLen", JInt maxLen ]
    | HoleValueSpace.Enum choices -> Canon.typed "Enum" [ "choices", JArr(List.map JStr choices) ]
    | HoleValueSpace.AnyString -> Canon.typed "AnyString" [  ]

and private encScalar (v: Scalar) : JVal =
    match v with
    | Scalar.Int value -> Canon.typed "Int" [ "value", JInt value ]
    | Scalar.Float value -> Canon.typed "Float" [ "value", JFloat value ]
    | Scalar.Bool value -> Canon.typed "Bool" [ "value", JBool value ]
    | Scalar.Str value -> Canon.typed "Str" [ "value", JStr value ]

and private encHoleDecl (v: HoleDecl) : JVal =
    match v with
    | HoleDecl.Value (name, space, ``default``) -> Canon.typed "Value" ([ Some("name", JStr name); Some("space", encHoleValueSpace space); (``default`` |> Option.map (fun v -> "default", encScalar v)) ] |> List.choose id)
    | HoleDecl.Slot (name, kindConstraint) -> Canon.typed "Slot" ([ Some("name", JStr name); (kindConstraint |> Option.map (fun v -> "kindConstraint", JStr v)) ] |> List.choose id)
    | HoleDecl.Repeat (name, countSpace) -> Canon.typed "Repeat" [ "name", JStr name; "countSpace", encHoleValueSpace countSpace ]

and private encFragmentArg<'Msg> (v: FragmentArg<'Msg>) : JVal =
    match v with
    | FragmentArg.Int value -> Canon.typed "Int" [ "value", JInt value ]
    | FragmentArg.Float value -> Canon.typed "Float" [ "value", JFloat value ]
    | FragmentArg.Bool value -> Canon.typed "Bool" [ "value", JBool value ]
    | FragmentArg.Str value -> Canon.typed "Str" [ "value", JStr value ]
    | FragmentArg.SlotArg tree -> Canon.typed "SlotArg" [ "tree", encNode tree ]

and private encCurveCommand (v: CurveCommand) : JVal =
    match v with
    | CurveCommand.MoveTo ``to`` -> Canon.typed "MoveTo" [ "to", encDrawPoint ``to`` ]
    | CurveCommand.LineTo ``to`` -> Canon.typed "LineTo" [ "to", encDrawPoint ``to`` ]
    | CurveCommand.CubicTo (control1, control2, ``to``) -> Canon.typed "CubicTo" [ "control1", encDrawPoint control1; "control2", encDrawPoint control2; "to", encDrawPoint ``to`` ]
    | CurveCommand.QuadraticTo (control, ``to``) -> Canon.typed "QuadraticTo" [ "control", encDrawPoint control; "to", encDrawPoint ``to`` ]
    | CurveCommand.Close -> Canon.typed "Close" [  ]

and private encShape (v: Shape) : JVal =
    match v with
    | Shape.Group (children, style) -> Canon.typed "Group" [ "children", JArr(List.map encShape children); "style", encDrawStyle style ]
    | Shape.Rectangle (x, y, width, height, cornerRadius, style) -> Canon.typed "Rectangle" ([ Some("x", JFloat x); Some("y", JFloat y); Some("width", JFloat width); Some("height", JFloat height); (cornerRadius |> Option.map (fun v -> "cornerRadius", JFloat v)); Some("style", encDrawStyle style) ] |> List.choose id)
    | Shape.Line (x1, y1, x2, y2, style) -> Canon.typed "Line" [ "x1", JFloat x1; "y1", JFloat y1; "x2", JFloat x2; "y2", JFloat y2; "style", encDrawStyle style ]
    | Shape.Polyline (points, style) -> Canon.typed "Polyline" [ "points", JArr(List.map encDrawPoint points); "style", encDrawStyle style ]
    | Shape.Polygon (points, style) -> Canon.typed "Polygon" [ "points", JArr(List.map encDrawPoint points); "style", encDrawStyle style ]
    | Shape.Curve (commands, style) -> Canon.typed "Curve" [ "commands", JArr(List.map encCurveCommand commands); "style", encDrawStyle style ]
    | Shape.Circle (cx, cy, r, style) -> Canon.typed "Circle" [ "cx", JFloat cx; "cy", JFloat cy; "r", JFloat r; "style", encDrawStyle style ]
    | Shape.Ellipse (cx, cy, rx, ry, style) -> Canon.typed "Ellipse" [ "cx", JFloat cx; "cy", JFloat cy; "rx", JFloat rx; "ry", JFloat ry; "style", encDrawStyle style ]
    | Shape.Label (x, y, text, style) -> Canon.typed "Label" [ "x", JFloat x; "y", JFloat y; "text", encTextSource text; "style", encDrawStyle style ]

and private encSemanticStyle (s: SemanticStyle) : JVal =
    JObj([ (if s.Emphasis = Emphasis.Normal then None else Some("emphasis", encEmphasis s.Emphasis)); (if s.Role = StyleRole.None then None else Some("role", encStyleRole s.Role)); (if s.Tone = ToneVariant.Default then None else Some("tone", encToneVariant s.Tone)); (if s.Voice = FontVoice.Default then None else Some("voice", encFontVoice s.Voice)); (if s.Weight = StyleWeight.Standard then None else Some("weight", encStyleWeight s.Weight)) ] |> List.choose id)

and private encStateBehaviour<'Msg> (s: StateBehaviour<'Msg>) : JVal =
    JObj([ (s.OnEmpty |> Option.map (fun v -> "onEmpty", encNode v)); (s.OnError |> Option.map (fun v -> "onError", JStr "<closure>")); (s.OnLoading |> Option.map (fun v -> "onLoading", encNode v)) ] |> List.choose id)

and private encAccessibility (s: Accessibility) : JVal =
    JObj([ (s.DescribedBy |> Option.map (fun v -> "describedBy", JStr v)); (s.Hidden |> Option.map (fun v -> "hidden", (encBinding JBool) v)); (s.Label |> Option.map (fun v -> "label", (encBinding JStr) v)); (s.LabelledBy |> Option.map (fun v -> "labelledBy", JStr v)); (s.LiveRegion |> Option.map (fun v -> "liveRegion", Fuaran.UI.HostPrelude.encLiveRegionKind v)); (s.Role |> Option.map (fun v -> "role", Fuaran.UI.HostPrelude.encAriaRole v)) ] |> List.choose id)

and private encSwitchCase<'Msg> (s: SwitchCase<'Msg>) : JVal =
    JObj([ Some("child", encNode s.Child); Some("match", JStr s.Match) ] |> List.choose id)

and private encGuestChannel (s: GuestChannel) : JVal =
    JObj([ Some("direction", encChannelDirection s.Direction); (s.MessageShape |> Option.map (fun v -> "messageShape", JStr v)) ] |> List.choose id)

and private encDrawPoint (s: DrawPoint) : JVal =
    JObj([ Some("x", JFloat s.X); Some("y", JFloat s.Y) ] |> List.choose id)

and private encViewBox (s: ViewBox) : JVal =
    JObj([ Some("height", JFloat s.Height); Some("minX", JFloat s.MinX); Some("minY", JFloat s.MinY); Some("width", JFloat s.Width) ] |> List.choose id)

and private encDrawStyle (s: DrawStyle) : JVal =
    JObj([ (s.Emphasis |> Option.map (fun v -> "emphasis", encEmphasis v)); (s.Fill |> Option.map (fun v -> "fill", (encBinding JStr) v)); (s.FontFamily |> Option.map (fun v -> "fontFamily", JStr v)); (s.FontSize |> Option.map (fun v -> "fontSize", JFloat v)); (s.MarkId |> Option.map (fun v -> "markId", JStr v)); (s.Opacity |> Option.map (fun v -> "opacity", (encBinding JFloat) v)); (s.Stroke |> Option.map (fun v -> "stroke", (encBinding JStr) v)); (s.StrokeWidth |> Option.map (fun v -> "strokeWidth", (encBinding JFloat) v)); (s.TextAnchor |> Option.map (fun v -> "textAnchor", encTextAnchor v)) ] |> List.choose id)

and private encInvokeArg (s: InvokeArg) : JVal =
    JObj([ Some("addr", JStr s.Addr); Some("value", JStr s.Value) ] |> List.choose id)

and private encSelectOption (s: SelectOption) : JVal =
    JObj([ Some("label", JStr s.Label); Some("value", JStr s.Value) ] |> List.choose id)

and private encMapMarker (s: MapMarker) : JVal =
    JObj([ Some("label", JStr s.Label); Some("latitude", JFloat s.Latitude); Some("longitude", JFloat s.Longitude) ] |> List.choose id)

and private encStaticRows (s: StaticRows) : JVal =
    JObj([ Some("headers", JArr(List.map encTextSource s.Headers)); Some("rows", JArr(List.map (fun __xs -> JArr(List.map encTextSource __xs)) s.Rows)) ] |> List.choose id)

and private encFormField<'Msg> (s: FormField<'Msg>) : JVal =
    JObj([ Some("id", JStr s.Id); Some("kind", encFormFieldKind s.Kind); Some("label", encTextSource s.Label); Some("required", JBool s.Required); (s.Help |> Option.map (fun v -> "help", encTextSource v)) ] |> List.choose id)

and private encFilterSpec<'Msg> (s: FilterSpec<'Msg>) : JVal =
    JObj([ Some("kind", encFormFieldKind s.Kind); Some("label", encTextSource s.Label); Some("name", JStr s.Name) ] |> List.choose id)

and private encTransformParam (s: TransformParam) : JVal =
    JObj([ Some("from", (encBinding id) s.From); Some("name", JStr s.Name) ] |> List.choose id)

and private encRangePair (s: RangePair) : JVal =
    JObj([ Some("max", JFloat s.Max); Some("min", JFloat s.Min) ] |> List.choose id)

and private encDateRangePair (s: DateRangePair) : JVal =
    JObj([ Some("from", JStr s.From); Some("to", JStr s.To) ] |> List.choose id)

and private encTabHeader (s: TabHeader) : JVal =
    JObj([ Some("label", encTextSource s.Label); (s.Icon |> Option.map (fun v -> "icon", JStr v)); (s.Disabled |> Option.map (fun v -> "disabled", (encBinding JBool) v)) ] |> List.choose id)

and private encColumnErased<'Msg> (s: ColumnErased<'Msg>) : JVal =
    JObj([ (s.Field |> Option.map (fun v -> "field", JStr v)); (match s.Format with | CellFormat.None -> None | _ -> Some("format", encCellFormat s.Format)); Some("kind", encCellKindErased s.Kind); Some("label", JStr s.Label); (s.Value |> Option.map (fun v -> "value", JStr "<closure>")); (match s.Width with | ColumnWidth.Auto -> None | _ -> Some("width", encColumnWidth s.Width)) ] |> List.choose id)

and private encButtonGroupItem<'Msg> (s: ButtonGroupItem<'Msg>) : JVal =
    JObj([ Some("label", encTextSource s.Label); (s.OnClick |> Option.map (fun v -> "onClick", JStr "<closure>")) ] |> List.choose id)

and private encContentHash (s: ContentHash) : JVal =
    JObj([ Some("algorithm", JStr s.Algorithm); Some("hash", JStr s.Hash); Some("strictness", encHashStrictness s.Strictness) ] |> List.choose id)

and private encEffectClass (s: EffectClass) : JVal =
    JObj([ Some("determinism", encDeterminismSource s.Determinism); Some("hostEffect", encHostEffect s.HostEffect) ] |> List.choose id)

and private encHeadingSpec (s: HeadingSpec) : JVal =
    Canon.typed "Heading" ([ Some("level", JInt s.Level); Some("text", encTextSource s.Text); Some("variant", encHeadingVariant s.Variant) ] |> List.choose id)

and private encBadgeSpec (s: BadgeSpec) : JVal =
    Canon.typed "Badge" ([ Some("label", encTextSource s.Label); Some("variant", encBadgeVariant s.Variant) ] |> List.choose id)

and private encMarkdownSpec (s: MarkdownSpec) : JVal =
    Canon.typed "Markdown" ([ Some("text", encTextSource s.Text) ] |> List.choose id)

and private encMathSpec (s: MathSpec) : JVal =
    Canon.typed "Math" ([ Some("source", JStr s.Source); Some("display", encMathDisplay s.Display) ] |> List.choose id)

and private encSkeletonSpec (s: SkeletonSpec) : JVal =
    Canon.typed "Skeleton" ([ Some("rows", JInt s.Rows) ] |> List.choose id)

and private encListSpec (s: ListSpec) : JVal =
    Canon.typed "List" ([ Some("items", JArr(List.map encTextSource s.Items)); Some("ordered", JBool s.Ordered) ] |> List.choose id)

and private encImageSpec (s: ImageSpec) : JVal =
    Canon.typed "Image" ([ Some("alt", encTextSource s.Alt); Some("src", (encBinding JStr) s.Src); Some("variant", encImageVariant s.Variant) ] |> List.choose id)

and private encLinkSpec (s: LinkSpec) : JVal =
    Canon.typed "Link" ([ Some("href", (encBinding JStr) s.Href); Some("label", encTextSource s.Label); Some("download", JBool s.Download); (s.Rel |> Option.map (fun v -> "rel", JStr v)); (s.Target |> Option.map (fun v -> "target", JStr v)) ] |> List.choose id)

and private encCalloutSpec (s: CalloutSpec) : JVal =
    Canon.typed "Callout" ([ Some("body", encTextSource s.Body); (if s.Dismissable = false then None else Some("dismissable", JBool s.Dismissable)); (if s.Tone = ToneVariant.Default then None else Some("tone", encToneVariant s.Tone)); (s.Heading |> Option.map (fun v -> "heading", encTextSource v)); (s.Icon |> Option.map (fun v -> "icon", JStr v)) ] |> List.choose id)

and private encProgressSpec (s: ProgressSpec) : JVal =
    Canon.typed "Progress" ([ Some("fraction", (encBinding JFloat) s.Fraction); (if s.Indeterminate = false then None else Some("indeterminate", JBool s.Indeterminate)); (if s.Tone = ToneVariant.Default then None else Some("tone", encToneVariant s.Tone)); (s.Label |> Option.map (fun v -> "label", encTextSource v)); (s.Caveat |> Option.map (fun v -> "caveat", encTextSource v)) ] |> List.choose id)

and private encMetricSpec (s: MetricSpec) : JVal =
    Canon.typed "Metric" ([ Some("label", encTextSource s.Label); Some("value", (encBinding JFloat) s.Value); (match s.Format with | CellFormat.None -> None | _ -> Some("format", encCellFormat s.Format)); (if s.Tone = ToneVariant.Default then None else Some("tone", encToneVariant s.Tone)); (if s.Weight = StyleWeight.Standard then None else Some("weight", encStyleWeight s.Weight)); (if s.Emphasis = Emphasis.Normal then None else Some("emphasis", encEmphasis s.Emphasis)); (s.Trend |> Option.map (fun v -> "trend", (encBinding JFloat) v)); (s.TrendFormat |> Option.map (fun v -> "trendFormat", encCellFormat v)); (s.Icon |> Option.map (fun v -> "icon", JStr v)); (s.Subtext |> Option.map (fun v -> "subtext", encTextSource v)) ] |> List.choose id)

and private encLabelValueRowSpec (s: LabelValueRowSpec) : JVal =
    Canon.typed "LabelValueRow" ([ (if s.Emphasis = false then None else Some("emphasis", JBool s.Emphasis)); (match s.Format with | CellFormat.None -> None | _ -> Some("format", encCellFormat s.Format)); Some("label", encTextSource s.Label); Some("value", (encBinding JFloat) s.Value); (s.Help |> Option.map (fun v -> "help", encTextSource v)) ] |> List.choose id)

and private encFactSpec (s: FactSpec) : JVal =
    Canon.typed "Fact" ([ (if s.Emphasis = false then None else Some("emphasis", JBool s.Emphasis)); (s.Help |> Option.map (fun v -> "help", encTextSource v)); (s.Icon |> Option.map (fun v -> "icon", JStr v)); Some("label", encTextSource s.Label); (if s.Tone = ToneVariant.Default then None else Some("tone", encToneVariant s.Tone)); Some("value", encTextSource s.Value) ] |> List.choose id)

and private encSparklineSpec (s: SparklineSpec) : JVal =
    Canon.typed "Sparkline" ([ Some("source", (encBinding (fun __xs -> JArr(List.map JFloat __xs))) s.Source) ] |> List.choose id)

and private encCodeBlockSpec (s: CodeBlockSpec) : JVal =
    Canon.typed "CodeBlock" ([ Some("code", JStr s.Code); Some("copyable", JBool s.Copyable); Some("highlightLines", JArr(List.map JInt s.HighlightLines)); Some("language", JStr s.Language); Some("lineNumbers", JBool s.LineNumbers) ] |> List.choose id)

and private encToastSpec (s: ToastSpec) : JVal =
    Canon.typed "Toast" ([ (if s.Dismissable = true then None else Some("dismissable", JBool s.Dismissable)); Some("message", encTextSource s.Message); Some("open", (encBinding JBool) s.Open); (if s.Tone = ToneVariant.Default then None else Some("tone", encToneVariant s.Tone)) ] |> List.choose id)

and private encDrawingSpec (s: DrawingSpec) : JVal =
    Canon.typed "Drawing" ([ (s.Description |> Option.map (fun v -> "description", encTextSource v)); Some("shapes", JArr(List.map encShape s.Shapes)); Some("style", encDrawStyle s.Style); (s.Title |> Option.map (fun v -> "title", encTextSource v)); Some("viewBox", encViewBox s.ViewBox) ] |> List.choose id)

and private encBoxSpec<'Msg> (s: BoxSpec<'Msg>) : JVal =
    Canon.typed "Box" ([ Some("children", JArr(List.map encNode s.Children)); (s.Heading |> Option.map (fun v -> "heading", encTextSource v)); Some("layout", encLayoutMode s.Layout); Some("role", encBoxRole s.Role) ] |> List.choose id)

and private encSplitPanelSpec<'Msg> (s: SplitPanelSpec<'Msg>) : JVal =
    Canon.typed "SplitPanel" ([ Some("children", JArr(List.map encNode s.Children)); Some("weight", JFloat s.Weight) ] |> List.choose id)

and private encSummaryListSpec<'Msg> (s: SummaryListSpec<'Msg>) : JVal =
    Canon.typed "SummaryList" ([ Some("children", JArr(List.map encNode s.Children)); (s.Heading |> Option.map (fun v -> "heading", encTextSource v)) ] |> List.choose id)

and private encDisclosureSpec<'Msg> (s: DisclosureSpec<'Msg>) : JVal =
    Canon.typed "Disclosure" ([ Some("children", JArr(List.map encNode s.Children)); Some("defaultOpen", JBool s.DefaultOpen); Some("heading", encTextSource s.Heading); (s.OnToggle |> Option.map (fun v -> "onToggle", JStr "<closure>")); Some("open", (encBinding JBool) s.Open) ] |> List.choose id)

and private encModalSpec<'Msg> (s: ModalSpec<'Msg>) : JVal =
    Canon.typed "Modal" ([ Some("children", JArr(List.map encNode s.Children)); Some("dismissable", JBool s.Dismissable); (s.OnDismiss |> Option.map (fun v -> "onDismiss", encAction v)); Some("open", (encBinding JBool) s.Open); (s.Heading |> Option.map (fun v -> "heading", encTextSource v)) ] |> List.choose id)

and private encScrollAreaSpec<'Msg> (s: ScrollAreaSpec<'Msg>) : JVal =
    Canon.typed "ScrollArea" ([ Some("children", JArr(List.map encNode s.Children)); Some("orientation", encScrollOrientation s.Orientation); (s.MaxHeight |> Option.map (fun v -> "maxHeight", JInt v)); (s.MaxWidth |> Option.map (fun v -> "maxWidth", JInt v)) ] |> List.choose id)

and private encTabsSpec<'Msg> (s: TabsSpec<'Msg>) : JVal =
    Canon.typed "Tabs" ([ Some("activeIndex", (encBinding JInt) s.ActiveIndex); Some("children", JArr(List.map encNode s.Children)); (if s.Orientation = Orientation.Horizontal then None else Some("orientation", encOrientation s.Orientation)); (s.OnSelect |> Option.map (fun v -> "onSelect", JStr "<closure>")); (s.OnSelectTag |> Option.map (fun v -> "onSelectTag", JStr "<closure>")); (s.TabHeaders |> Option.map (fun v -> "tabHeaders", JArr(List.map encTabHeader v))); (s.TabTags |> Option.map (fun v -> "tabTags", JArr(List.map JStr v))); (s.ActiveTag |> Option.map (fun v -> "activeTag", (encBinding JStr) v)) ] |> List.choose id)

and private encStepperSpec<'Msg> (s: StepperSpec<'Msg>) : JVal =
    Canon.typed "Stepper" ([ Some("activeStep", (encBinding JInt) s.ActiveStep); Some("children", JArr(List.map encNode s.Children)); (s.OnSelect |> Option.map (fun v -> "onSelect", JStr "<closure>")) ] |> List.choose id)

and private encButtonSpec<'Msg> (s: ButtonSpec<'Msg>) : JVal =
    Canon.typed "Button" ([ Some("label", encTextSource s.Label); Some("onClick", encAction s.OnClick); Some("variant", encButtonVariant s.Variant); (s.Icon |> Option.map (fun v -> "icon", JStr v)); None; (s.Disabled |> Option.map (fun v -> "disabled", (encBinding JBool) v)) ] |> List.choose id)

and private encSelectSpec<'Msg> (s: SelectSpec<'Msg>) : JVal =
    Canon.typed "Select" ([ Some("label", encTextSource s.Label); (s.OnChange |> Option.map (fun v -> "onChange", JStr "<closure>")); (s.OnChangeMulti |> Option.map (fun v -> "onChangeMulti", JStr "<closure>")); Some("source", (encBinding (fun __xs -> JArr(List.map encSelectOption __xs))) s.Source); Some("value", (encBinding JStr) s.Value); (s.Placeholder |> Option.map (fun v -> "placeholder", encTextSource v)); (s.Disabled |> Option.map (fun v -> "disabled", (encBinding JBool) v)); (s.Multiple |> Option.map (fun v -> "multiple", JBool v)); (s.Values |> Option.map (fun v -> "values", (encBinding (fun __xs -> JArr(List.map JStr __xs))) v)) ] |> List.choose id)

and private encFileUploadSpec<'Msg> (s: FileUploadSpec<'Msg>) : JVal =
    Canon.typed "FileUpload" ([ Some("accept", JArr(List.map JStr s.Accept)); Some("label", encTextSource s.Label); Some("multiple", JBool s.Multiple); (s.OnSelect |> Option.map (fun v -> "onSelect", JStr "<closure>")); (s.Disabled |> Option.map (fun v -> "disabled", (encBinding JBool) v)) ] |> List.choose id)

and private encFormSpec<'Msg> (s: FormSpec<'Msg>) : JVal =
    Canon.typed "Form" ([ Some("fields", JArr(List.map encFormField s.Fields)); Some("onSubmit", encAction s.OnSubmit); Some("submitLabel", encTextSource s.SubmitLabel); (s.Disabled |> Option.map (fun v -> "disabled", (encBinding JBool) v)) ] |> List.choose id)

and private encFiltersSpec<'Msg> (s: FiltersSpec<'Msg>) : JVal =
    Canon.typed "Filters" ([ Some("items", JArr(List.map encFilterSpec s.Items)) ] |> List.choose id)

and private encDataGridSpec<'Msg> (s: DataGridSpec<'Msg>) : JVal =
    Canon.typed "DataGrid" ([ Some("columns", JArr(List.map encColumnErased s.Columns)); (if s.Editable = false then None else Some("editable", JBool s.Editable)); (s.RowKey |> Option.map (fun v -> "rowKey", JStr "<closure>")); (s.RowKeyField |> Option.map (fun v -> "rowKeyField", JStr v)); Some("source", (encBinding (fun (_: obj seq) -> JStr "<opaque>")) s.Source); (s.StaticRows |> Option.map (fun v -> "staticRows", encStaticRows v)); (s.OnRowClick |> Option.map (fun v -> "onRowClick", JStr "<closure>")) ] |> List.choose id)

and private encChartSpec<'Msg> (s: ChartSpec<'Msg>) : JVal =
    Canon.typed "Chart" ([ Some("kind", encChartKind s.Kind); Some("source", (encBinding (fun (_: obj seq) -> JStr "<opaque>")) s.Source); Some("stacked", JBool s.Stacked); Some("xField", JStr s.XField); Some("yFields", JArr(List.map JStr s.YFields)); (s.Title |> Option.map (fun v -> "title", encTextSource v)); (s.OnPointClick |> Option.map (fun v -> "onPointClick", JStr "<closure>")) ] |> List.choose id)

and private encMapSpec<'Msg> (s: MapSpec<'Msg>) : JVal =
    Canon.typed "Map" ([ Some("centreLatitude", JFloat s.CentreLatitude); Some("centreLongitude", JFloat s.CentreLongitude); Some("source", (encBinding (fun __xs -> JArr(List.map encMapMarker __xs))) s.Source); Some("zoom", JInt s.Zoom); (s.OnMarkerClick |> Option.map (fun v -> "onMarkerClick", JStr "<closure>")) ] |> List.choose id)

and private encCustomSpec (s: CustomSpec) : JVal =
    Canon.typed "Custom" ([ Some("moduleId", JStr s.ModuleId); Some("componentId", JStr s.ComponentId); Some("props", (fun __m -> JObj(Map.toList __m |> List.map (fun (k, v) -> k, id v))) s.Props); (s.ContentHash |> Option.map (fun v -> "contentHash", encContentHash v)); (s.ExposedNodeIds |> Option.map (fun v -> "exposedNodeIds", JArr(List.map JStr v))) ] |> List.choose id)

and private encErrorBoundarySpec<'Msg> (s: ErrorBoundarySpec<'Msg>) : JVal =
    Canon.typed "ErrorBoundary" ([ Some("child", encNode s.Child); Some("fallback", encNode s.Fallback) ] |> List.choose id)

and private encFragmentDeclSpec<'Msg> (s: FragmentDeclSpec<'Msg>) : JVal =
    Canon.typed "FragmentDecl" ([ Some("body", encNode s.Body); Some("name", JStr s.Name); (s.Holes |> Option.map (fun v -> "holes", JArr(List.map encHoleDecl v))); (s.Effect |> Option.map (fun v -> "effect", encEffectClass v)) ] |> List.choose id)

and private encFragmentRefSpec<'Msg> (s: FragmentRefSpec<'Msg>) : JVal =
    Canon.typed "FragmentRef" ([ Some("name", JStr s.Name); (s.Args |> Option.map (fun v -> "args", (fun __m -> JObj(Map.toList __m |> List.map (fun (k, v) -> k, encFragmentArg v))) v)) ] |> List.choose id)

and private encSwitchSpec<'Msg> (s: SwitchSpec<'Msg>) : JVal =
    Canon.typed "Switch" ([ Some("cases", JArr(List.map encSwitchCase s.Cases)); Some("default", encNode s.Default); Some("stateKey", JStr s.StateKey) ] |> List.choose id)

and private encMountSpec<'Msg> (s: MountSpec<'Msg>) : JVal =
    Canon.typed "Mount" ([ Some("capabilities", JArr(List.map JStr s.Capabilities)); Some("channel", encGuestChannel s.Channel); (s.Inputs |> Option.map (fun v -> "inputs", (fun __m -> JObj(Map.toList __m |> List.map (fun (k, v) -> k, encFragmentArg v))) v)); (s.OnBubble |> Option.map (fun v -> "onBubble", JStr "<closure>")); Some("scopeId", JStr s.ScopeId) ] |> List.choose id)

let encodeNode (n: Node<'Msg>) : string = Canon.render (encNode n)

/// JVal-level accessors (Phase 694) — for host codecs that splice generated
/// encodings into a larger canonical document (e.g. a TreeOp codec).
let encodeNodeJson (n: Node<'Msg>) : JVal = encNode n

let encodeNodeKindJson (k: NodeKind<'Msg>) : JVal = encNodeKind k

let encodeStateBehaviourJson (s: StateBehaviour<'Msg>) : JVal = encStateBehaviour s

let encodeSemanticStyleJson (s: SemanticStyle) : JVal = encSemanticStyle s

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
let private dFloat (j: JVal) : Result<float, string> =
    match j with
    | JFloat f -> Ok f
    | JInt i -> Ok(float i)
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

let private decHeadingVariant (j: JVal) : Result<HeadingVariant, string> =
    match j with
    | JStr "Standard" -> Ok HeadingVariant.Standard
    | JStr "Eyebrow" -> Ok HeadingVariant.Eyebrow
    | JStr "Caption" -> Ok HeadingVariant.Caption
    | JStr "Lead" -> Ok HeadingVariant.Lead
    | _ -> Error "not a HeadingVariant"

let private decBadgeVariant (j: JVal) : Result<BadgeVariant, string> =
    match j with
    | JStr "Neutral" -> Ok BadgeVariant.Neutral
    | JStr "Brand" -> Ok BadgeVariant.Brand
    | JStr "Success" -> Ok BadgeVariant.Success
    | JStr "Warning" -> Ok BadgeVariant.Warning
    | JStr "Critical" -> Ok BadgeVariant.Critical
    | JStr "Info" -> Ok BadgeVariant.Info
    | _ -> Error "not a BadgeVariant"

let private decOrientation (j: JVal) : Result<Orientation, string> =
    match j with
    | JStr "Vertical" -> Ok Orientation.Vertical
    | JStr "Horizontal" -> Ok Orientation.Horizontal
    | _ -> Error "not a Orientation"

let private decBoxRole (j: JVal) : Result<BoxRole, string> =
    match j with
    | JStr "Dashboard" -> Ok BoxRole.Dashboard
    | JStr "Card" -> Ok BoxRole.Card
    | JStr "Group" -> Ok BoxRole.Group
    | JStr "Separator" -> Ok BoxRole.Separator
    | _ -> Error "not a BoxRole"

let private decMathDisplay (j: JVal) : Result<MathDisplay, string> =
    match j with
    | JStr "Inline" -> Ok MathDisplay.Inline
    | JStr "Block" -> Ok MathDisplay.Block
    | _ -> Error "not a MathDisplay"

let private decImageVariant (j: JVal) : Result<ImageVariant, string> =
    match j with
    | JStr "Default" -> Ok ImageVariant.Default
    | JStr "Avatar" -> Ok ImageVariant.Avatar
    | JStr "Rounded" -> Ok ImageVariant.Rounded
    | _ -> Error "not a ImageVariant"

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

let private decStyleWeight (j: JVal) : Result<StyleWeight, string> =
    match j with
    | JStr "Compact" -> Ok StyleWeight.Compact
    | JStr "Standard" -> Ok StyleWeight.Standard
    | JStr "Spacious" -> Ok StyleWeight.Spacious
    | _ -> Error "not a StyleWeight"

let private decEmphasis (j: JVal) : Result<Emphasis, string> =
    match j with
    | JStr "Quiet" -> Ok Emphasis.Quiet
    | JStr "Normal" -> Ok Emphasis.Normal
    | JStr "Loud" -> Ok Emphasis.Loud
    | _ -> Error "not a Emphasis"

let private decScrollOrientation (j: JVal) : Result<ScrollOrientation, string> =
    match j with
    | JStr "Vertical" -> Ok ScrollOrientation.Vertical
    | JStr "Horizontal" -> Ok ScrollOrientation.Horizontal
    | JStr "Both" -> Ok ScrollOrientation.Both
    | _ -> Error "not a ScrollOrientation"

let private decButtonVariant (j: JVal) : Result<ButtonVariant, string> =
    match j with
    | JStr "Primary" -> Ok ButtonVariant.Primary
    | JStr "Secondary" -> Ok ButtonVariant.Secondary
    | JStr "Tertiary" -> Ok ButtonVariant.Tertiary
    | JStr "Destructive" -> Ok ButtonVariant.Destructive
    | _ -> Error "not a ButtonVariant"

let private decFileReadEncoding (j: JVal) : Result<FileReadEncoding, string> =
    match j with
    | JStr "Text" -> Ok FileReadEncoding.Text
    | JStr "Base64" -> Ok FileReadEncoding.Base64
    | JStr "DataUrl" -> Ok FileReadEncoding.DataUrl
    | _ -> Error "not a FileReadEncoding"

let private decDateVariant (j: JVal) : Result<DateVariant, string> =
    match j with
    | JStr "Date" -> Ok DateVariant.Date
    | JStr "Time" -> Ok DateVariant.Time
    | JStr "DateTime" -> Ok DateVariant.DateTime
    | _ -> Error "not a DateVariant"

let private decDateStyle (j: JVal) : Result<DateStyle, string> =
    match j with
    | JStr "Short" -> Ok DateStyle.Short
    | JStr "Medium" -> Ok DateStyle.Medium
    | JStr "Long" -> Ok DateStyle.Long
    | JStr "Full" -> Ok DateStyle.Full
    | _ -> Error "not a DateStyle"

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

let private decChartKind (j: JVal) : Result<ChartKind, string> =
    match j with
    | JStr "Line" -> Ok ChartKind.Line
    | JStr "Bar" -> Ok ChartKind.Bar
    | JStr "Area" -> Ok ChartKind.Area
    | JStr "Pie" -> Ok ChartKind.Pie
    | JStr "Scatter" -> Ok ChartKind.Scatter
    | JStr "Heatmap" -> Ok ChartKind.Heatmap
    | _ -> Error "not a ChartKind"

let private decHashStrictness (j: JVal) : Result<HashStrictness, string> =
    match j with
    | JStr "StrictReplay" -> Ok HashStrictness.StrictReplay
    | JStr "AdvisoryWarning" -> Ok HashStrictness.AdvisoryWarning
    | JStr "Enforced" -> Ok HashStrictness.Enforced
    | _ -> Error "not a HashStrictness"

let private decHostEffect (j: JVal) : Result<HostEffect, string> =
    match j with
    | JStr "Pure" -> Ok HostEffect.Pure
    | JStr "ReadsHost" -> Ok HostEffect.ReadsHost
    | JStr "WritesHost" -> Ok HostEffect.WritesHost
    | _ -> Error "not a HostEffect"

let private decDeterminismSource (j: JVal) : Result<DeterminismSource, string> =
    match j with
    | JStr "Deterministic" -> Ok DeterminismSource.Deterministic
    | JStr "Clock" -> Ok DeterminismSource.Clock
    | JStr "Random" -> Ok DeterminismSource.Random
    | JStr "Network" -> Ok DeterminismSource.Network
    | _ -> Error "not a DeterminismSource"

let private decChannelDirection (j: JVal) : Result<ChannelDirection, string> =
    match j with
    | JStr "OutOnly" -> Ok ChannelDirection.OutOnly
    | JStr "TwoWay" -> Ok ChannelDirection.TwoWay
    | _ -> Error "not a ChannelDirection"

let private decTextAnchor (j: JVal) : Result<TextAnchor, string> =
    match j with
    | JStr "Start" -> Ok TextAnchor.Start
    | JStr "Middle" -> Ok TextAnchor.Middle
    | JStr "End" -> Ok TextAnchor.End
    | _ -> Error "not a TextAnchor"

let private decStyleRole (j: JVal) : Result<StyleRole, string> =
    match j with
    | JStr "None" -> Ok StyleRole.None
    | JStr "Eyebrow" -> Ok StyleRole.Eyebrow
    | JStr "Data" -> Ok StyleRole.Data
    | JStr "Lede" -> Ok StyleRole.Lede
    | JStr "Caption" -> Ok StyleRole.Caption
    | _ -> Error "not a StyleRole"

let private decFontVoice (j: JVal) : Result<FontVoice, string> =
    match j with
    | JStr "Default" -> Ok FontVoice.Default
    | JStr "Display" -> Ok FontVoice.Display
    | JStr "Structural" -> Ok FontVoice.Structural
    | _ -> Error "not a FontVoice"

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
    | _ -> Error "not a Motion"

let rec private decNodeKind (j: JVal) : Result<NodeKind<obj>, string> =
    dObj j |> Result.bind (fun __fs ->
    dTag __fs |> Result.bind (fun __t ->
    match __t with
    | "Heading" -> decHeadingSpec j |> Result.map NodeKind.Heading
    | "Badge" -> decBadgeSpec j |> Result.map NodeKind.Badge
    | "Markdown" -> decMarkdownSpec j |> Result.map NodeKind.Markdown
    | "Math" -> decMathSpec j |> Result.map NodeKind.Math
    | "Skeleton" -> decSkeletonSpec j |> Result.map NodeKind.Skeleton
    | "List" -> decListSpec j |> Result.map NodeKind.List
    | "Image" -> decImageSpec j |> Result.map NodeKind.Image
    | "Link" -> decLinkSpec j |> Result.map NodeKind.Link
    | "Callout" -> decCalloutSpec j |> Result.map NodeKind.Callout
    | "Progress" -> decProgressSpec j |> Result.map NodeKind.Progress
    | "Metric" -> decMetricSpec j |> Result.map NodeKind.Metric
    | "LabelValueRow" -> decLabelValueRowSpec j |> Result.map NodeKind.LabelValueRow
    | "Fact" -> decFactSpec j |> Result.map NodeKind.Fact
    | "Sparkline" -> decSparklineSpec j |> Result.map NodeKind.Sparkline
    | "CodeBlock" -> decCodeBlockSpec j |> Result.map NodeKind.CodeBlock
    | "Toast" -> decToastSpec j |> Result.map NodeKind.Toast
    | "Drawing" -> decDrawingSpec j |> Result.map NodeKind.Drawing
    | "Box" -> decBoxSpec j |> Result.map NodeKind.Box
    | "SplitPanel" -> decSplitPanelSpec j |> Result.map NodeKind.SplitPanel
    | "SummaryList" -> decSummaryListSpec j |> Result.map NodeKind.SummaryList
    | "Disclosure" -> decDisclosureSpec j |> Result.map NodeKind.Disclosure
    | "Modal" -> decModalSpec j |> Result.map NodeKind.Modal
    | "ScrollArea" -> decScrollAreaSpec j |> Result.map NodeKind.ScrollArea
    | "Tabs" -> decTabsSpec j |> Result.map NodeKind.Tabs
    | "Stepper" -> decStepperSpec j |> Result.map NodeKind.Stepper
    | "Button" -> decButtonSpec j |> Result.map NodeKind.Button
    | "Select" -> decSelectSpec j |> Result.map NodeKind.Select
    | "FileUpload" -> decFileUploadSpec j |> Result.map NodeKind.FileUpload
    | "Form" -> decFormSpec j |> Result.map NodeKind.Form
    | "Filters" -> decFiltersSpec j |> Result.map NodeKind.Filters
    | "DataGrid" -> decDataGridSpec j |> Result.map NodeKind.DataGrid
    | "Chart" -> decChartSpec j |> Result.map NodeKind.Chart
    | "Map" -> decMapSpec j |> Result.map NodeKind.Map
    | "Custom" -> decCustomSpec j |> Result.map NodeKind.Custom
    | "ErrorBoundary" -> decErrorBoundarySpec j |> Result.map NodeKind.ErrorBoundary
    | "FragmentDecl" -> decFragmentDeclSpec j |> Result.map NodeKind.FragmentDecl
    | "FragmentRef" -> decFragmentRefSpec j |> Result.map NodeKind.FragmentRef
    | "Switch" -> decSwitchSpec j |> Result.map NodeKind.Switch
    | "Mount" -> decMountSpec j |> Result.map NodeKind.Mount
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
    Ok { Id = id; Kind = kind; Accessibility = accessibility; ExtraAttributes = extraAttributes; Motion = motion; State = state; Style = style }))))))))

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
        | "Transform" ->
            dReq "source" __fs (fun __j -> Fuaran.Core.ColumnCodec.decodeJson __j |> Result.mapError string) |> Result.bind (fun source ->
            dReq "pipeline" __fs (dList (fun __j -> Fuaran.Core.DataFrameCodec.decodeTransform __j |> Result.mapError string)) |> Result.bind (fun pipeline ->
            dOpt "params" __fs (dList decTransformParam) |> Result.bind (fun ``params`` ->
            Ok(Binding.Transform(source, pipeline, ``params``)))))
        | "Invoke" ->
            dReq "capabilityId" __fs dStr |> Result.bind (fun capabilityId ->
            dReq "args" __fs (dList decInvokeArg) |> Result.bind (fun args ->
            Ok(Binding.Invoke(capabilityId, args))))
        | __other -> Error ("unknown Binding case: " + __other))
    | _ -> Error "expected a Binding object"

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
        | "Custom" ->
            Ok ((fun _ -> "")) |> Result.bind (fun fn ->
            Ok(CellFormat.Custom(fn)))
        | __other -> Error ("unknown CellFormat case: " + __other))
    | _ -> Error "expected a CellFormat object"

and private decAction (j: JVal) : Result<Action<obj>, string> =
    match j with
    | JObj __fs when (__fs |> List.exists (fun (k, _) -> k = "$type")) ->
        dTag __fs |> Result.bind (fun __t ->
        match __t with
        | "Chain" ->
            dReq "ops" __fs (dList decAction) |> Result.bind (fun ops ->
            Ok(Action.Chain(ops)))
        | "WriteToClipboard" ->
            dReq "text" __fs dStr |> Result.bind (fun text ->
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
            dReq "value" __fs dJson |> Result.bind (fun value ->
            Ok(Action.SetState(key, value))))
        | "AiTool" ->
            dReq "toolName" __fs dStr |> Result.bind (fun toolName ->
            dReq "args" __fs dJson |> Result.bind (fun args ->
            Ok(Action.AiTool(toolName, args))))
        | __other -> Error ("unknown Action case: " + __other))
    | _ -> Error "expected a Action object"

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
        | __other -> Error ("unknown Format case: " + __other))
    | _ -> Error "expected a Format object"

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
        | __other -> Error ("unknown LayoutMode case: " + __other))
    | _ -> Error "expected a LayoutMode object"

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
        | __other -> Error ("unknown FormFieldKind case: " + __other))
    | _ -> Error "expected a FormFieldKind object"

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

and private decCellKindErased (j: JVal) : Result<CellKindErased<obj>, string> =
    match j with
    | JObj __fs when (__fs |> List.exists (fun (k, _) -> k = "$type")) ->
        dTag __fs |> Result.bind (fun __t ->
        match __t with
        | "Text" -> Ok CellKindErased.Text
        | "Numeric" -> Ok CellKindErased.Numeric
        | "Date" -> Ok CellKindErased.Date
        | "Editable" ->
            (dPresent "onEdit" __fs |> Result.map (Option.map (fun () -> (fun (_: obj * Fuaran.UI.HostPrelude.CellValue) -> Action.Chain [])))) |> Result.bind (fun onEdit ->
            Ok(CellKindErased.Editable(onEdit)))
        | "Checkbox" ->
            Ok ((fun _ -> false)) |> Result.bind (fun get ->
            (dPresent "onToggle" __fs |> Result.map (Option.map (fun () -> (fun (_: obj * bool) -> Action.Chain [])))) |> Result.bind (fun onToggle ->
            Ok(CellKindErased.Checkbox(get, onToggle))))
        | "Button" ->
            dReq "label" __fs decTextSource |> Result.bind (fun label ->
            (dPresent "onClick" __fs |> Result.map (Option.map (fun () -> (fun (_: obj) -> Action.Chain [])))) |> Result.bind (fun onClick ->
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
        | "Progress" ->
            Ok ((fun _ -> 0.0)) |> Result.bind (fun fractionFn ->
            (dPresent "labelFn" __fs |> Result.map (Option.map (fun () -> (fun _ -> TextSource.Literal "")))) |> Result.bind (fun labelFn ->
            Ok(CellKindErased.Progress(fractionFn, labelFn))))
        | "Custom" ->
            Ok ((fun _ -> Unchecked.defaultof<Node<obj>>)) |> Result.bind (fun fn ->
            Ok(CellKindErased.Custom(fn)))
        | __other -> Error ("unknown CellKindErased case: " + __other))
    | _ -> Error "expected a CellKindErased object"

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

and private decSemanticStyle (j: JVal) : Result<SemanticStyle, string> =
    dObj j |> Result.bind (fun __fs ->
    dDef "emphasis" __fs decEmphasis (Emphasis.Normal) |> Result.bind (fun emphasis ->
    dDef "role" __fs decStyleRole (StyleRole.None) |> Result.bind (fun role ->
    dDef "tone" __fs decToneVariant (ToneVariant.Default) |> Result.bind (fun tone ->
    dDef "voice" __fs decFontVoice (FontVoice.Default) |> Result.bind (fun voice ->
    dDef "weight" __fs decStyleWeight (StyleWeight.Standard) |> Result.bind (fun weight ->
    Ok { Emphasis = emphasis; Role = role; Tone = tone; Voice = voice; Weight = weight }))))))

and private decStateBehaviour (j: JVal) : Result<StateBehaviour<obj>, string> =
    dObj j |> Result.bind (fun __fs ->
    dOpt "onEmpty" __fs decNode |> Result.bind (fun onEmpty ->
    (dPresent "onError" __fs |> Result.map (Option.map (fun () -> (fun _ -> Unchecked.defaultof<Node<obj>>)))) |> Result.bind (fun onError ->
    dOpt "onLoading" __fs decNode |> Result.bind (fun onLoading ->
    Ok { OnEmpty = onEmpty; OnError = onError; OnLoading = onLoading }))))

and private decAccessibility (j: JVal) : Result<Accessibility, string> =
    dObj j |> Result.bind (fun __fs ->
    dOpt "describedBy" __fs dStr |> Result.bind (fun describedBy ->
    dOpt "hidden" __fs (decBinding dBool) |> Result.bind (fun hidden ->
    dOpt "label" __fs (decBinding dStr) |> Result.bind (fun label ->
    dOpt "labelledBy" __fs dStr |> Result.bind (fun labelledBy ->
    dOpt "liveRegion" __fs Fuaran.UI.HostPrelude.decLiveRegionKind |> Result.bind (fun liveRegion ->
    dOpt "role" __fs Fuaran.UI.HostPrelude.decAriaRole |> Result.bind (fun role ->
    Ok { DescribedBy = describedBy; Hidden = hidden; Label = label; LabelledBy = labelledBy; LiveRegion = liveRegion; Role = role })))))))

and private decSwitchCase (j: JVal) : Result<SwitchCase<obj>, string> =
    dObj j |> Result.bind (fun __fs ->
    dReq "child" __fs decNode |> Result.bind (fun child ->
    dReq "match" __fs dStr |> Result.bind (fun ``match`` ->
    Ok { Child = child; Match = ``match`` })))

and private decGuestChannel (j: JVal) : Result<GuestChannel, string> =
    dObj j |> Result.bind (fun __fs ->
    dReq "direction" __fs decChannelDirection |> Result.bind (fun direction ->
    dOpt "messageShape" __fs dStr |> Result.bind (fun messageShape ->
    Ok { Direction = direction; MessageShape = messageShape })))

and private decDrawPoint (j: JVal) : Result<DrawPoint, string> =
    dObj j |> Result.bind (fun __fs ->
    dReq "x" __fs dFloat |> Result.bind (fun x ->
    dReq "y" __fs dFloat |> Result.bind (fun y ->
    Ok { X = x; Y = y })))

and private decViewBox (j: JVal) : Result<ViewBox, string> =
    dObj j |> Result.bind (fun __fs ->
    dReq "height" __fs dFloat |> Result.bind (fun height ->
    dReq "minX" __fs dFloat |> Result.bind (fun minX ->
    dReq "minY" __fs dFloat |> Result.bind (fun minY ->
    dReq "width" __fs dFloat |> Result.bind (fun width ->
    Ok { Height = height; MinX = minX; MinY = minY; Width = width })))))

and private decDrawStyle (j: JVal) : Result<DrawStyle, string> =
    dObj j |> Result.bind (fun __fs ->
    dOpt "emphasis" __fs decEmphasis |> Result.bind (fun emphasis ->
    dOpt "fill" __fs (decBinding dStr) |> Result.bind (fun fill ->
    dOpt "fontFamily" __fs dStr |> Result.bind (fun fontFamily ->
    dOpt "fontSize" __fs dFloat |> Result.bind (fun fontSize ->
    dOpt "markId" __fs dStr |> Result.bind (fun markId ->
    dOpt "opacity" __fs (decBinding dFloat) |> Result.bind (fun opacity ->
    dOpt "stroke" __fs (decBinding dStr) |> Result.bind (fun stroke ->
    dOpt "strokeWidth" __fs (decBinding dFloat) |> Result.bind (fun strokeWidth ->
    dOpt "textAnchor" __fs decTextAnchor |> Result.bind (fun textAnchor ->
    Ok { Emphasis = emphasis; Fill = fill; FontFamily = fontFamily; FontSize = fontSize; MarkId = markId; Opacity = opacity; Stroke = stroke; StrokeWidth = strokeWidth; TextAnchor = textAnchor }))))))))))

and private decInvokeArg (j: JVal) : Result<InvokeArg, string> =
    dObj j |> Result.bind (fun __fs ->
    dReq "addr" __fs dStr |> Result.bind (fun addr ->
    dReq "value" __fs dStr |> Result.bind (fun value ->
    Ok { Addr = addr; Value = value })))

and private decSelectOption (j: JVal) : Result<SelectOption, string> =
    dObj j |> Result.bind (fun __fs ->
    dReq "label" __fs dStr |> Result.bind (fun label ->
    dReq "value" __fs dStr |> Result.bind (fun value ->
    Ok { Label = label; Value = value })))

and private decMapMarker (j: JVal) : Result<MapMarker, string> =
    dObj j |> Result.bind (fun __fs ->
    dReq "label" __fs dStr |> Result.bind (fun label ->
    dReq "latitude" __fs dFloat |> Result.bind (fun latitude ->
    dReq "longitude" __fs dFloat |> Result.bind (fun longitude ->
    Ok { Label = label; Latitude = latitude; Longitude = longitude }))))

and private decStaticRows (j: JVal) : Result<StaticRows, string> =
    dObj j |> Result.bind (fun __fs ->
    dReq "headers" __fs (dList decTextSource) |> Result.bind (fun headers ->
    dReq "rows" __fs (dList (dList decTextSource)) |> Result.bind (fun rows ->
    Ok { Headers = headers; Rows = rows })))

and private decFormField (j: JVal) : Result<FormField<obj>, string> =
    dObj j |> Result.bind (fun __fs ->
    dReq "id" __fs dStr |> Result.bind (fun id ->
    dReq "kind" __fs decFormFieldKind |> Result.bind (fun kind ->
    dReq "label" __fs decTextSource |> Result.bind (fun label ->
    dReq "required" __fs dBool |> Result.bind (fun required ->
    dOpt "help" __fs decTextSource |> Result.bind (fun help ->
    Ok { Id = id; Kind = kind; Label = label; Required = required; Help = help }))))))

and private decFilterSpec (j: JVal) : Result<FilterSpec<obj>, string> =
    dObj j |> Result.bind (fun __fs ->
    dReq "kind" __fs decFormFieldKind |> Result.bind (fun kind ->
    dReq "label" __fs decTextSource |> Result.bind (fun label ->
    dReq "name" __fs dStr |> Result.bind (fun name ->
    Ok { Kind = kind; Label = label; Name = name }))))

and private decTransformParam (j: JVal) : Result<TransformParam, string> =
    dObj j |> Result.bind (fun __fs ->
    dReq "from" __fs (decBinding dJson) |> Result.bind (fun from ->
    dReq "name" __fs dStr |> Result.bind (fun name ->
    Ok { From = from; Name = name })))

and private decRangePair (j: JVal) : Result<RangePair, string> =
    dObj j |> Result.bind (fun __fs ->
    dReq "max" __fs dFloat |> Result.bind (fun max ->
    dReq "min" __fs dFloat |> Result.bind (fun min ->
    Ok { Max = max; Min = min })))

and private decDateRangePair (j: JVal) : Result<DateRangePair, string> =
    dObj j |> Result.bind (fun __fs ->
    dReq "from" __fs dStr |> Result.bind (fun from ->
    dReq "to" __fs dStr |> Result.bind (fun ``to`` ->
    Ok { From = from; To = ``to`` })))

and private decTabHeader (j: JVal) : Result<TabHeader, string> =
    dObj j |> Result.bind (fun __fs ->
    dReq "label" __fs decTextSource |> Result.bind (fun label ->
    dOpt "icon" __fs dStr |> Result.bind (fun icon ->
    dOpt "disabled" __fs (decBinding dBool) |> Result.bind (fun disabled ->
    Ok { Label = label; Icon = icon; Disabled = disabled }))))

and private decColumnErased (j: JVal) : Result<ColumnErased<obj>, string> =
    dObj j |> Result.bind (fun __fs ->
    dOpt "field" __fs dStr |> Result.bind (fun field ->
    dDef "format" __fs decCellFormat (CellFormat.None) |> Result.bind (fun format ->
    dReq "kind" __fs decCellKindErased |> Result.bind (fun kind ->
    dReq "label" __fs dStr |> Result.bind (fun label ->
    (dPresent "value" __fs |> Result.map (Option.map (fun () -> (fun _ -> Fuaran.UI.HostPrelude.CellValue.Empty)))) |> Result.bind (fun value ->
    dDef "width" __fs decColumnWidth (ColumnWidth.Auto) |> Result.bind (fun width ->
    Ok { Field = field; Format = format; Kind = kind; Label = label; Value = value; Width = width })))))))

and private decButtonGroupItem (j: JVal) : Result<ButtonGroupItem<obj>, string> =
    dObj j |> Result.bind (fun __fs ->
    dReq "label" __fs decTextSource |> Result.bind (fun label ->
    (dPresent "onClick" __fs |> Result.map (Option.map (fun () -> (fun (_: obj) -> Action.Chain [])))) |> Result.bind (fun onClick ->
    Ok { Label = label; OnClick = onClick })))

and private decContentHash (j: JVal) : Result<ContentHash, string> =
    dObj j |> Result.bind (fun __fs ->
    dReq "algorithm" __fs dStr |> Result.bind (fun algorithm ->
    dReq "hash" __fs dStr |> Result.bind (fun hash ->
    dReq "strictness" __fs decHashStrictness |> Result.bind (fun strictness ->
    Ok { Algorithm = algorithm; Hash = hash; Strictness = strictness }))))

and private decEffectClass (j: JVal) : Result<EffectClass, string> =
    dObj j |> Result.bind (fun __fs ->
    dReq "determinism" __fs decDeterminismSource |> Result.bind (fun determinism ->
    dReq "hostEffect" __fs decHostEffect |> Result.bind (fun hostEffect ->
    Ok { Determinism = determinism; HostEffect = hostEffect })))

and private decHeadingSpec (j: JVal) : Result<HeadingSpec, string> =
    dObj j |> Result.bind (fun __fs ->
    dReq "level" __fs dInt |> Result.bind (fun level ->
    dReq "text" __fs decTextSource |> Result.bind (fun text ->
    dReq "variant" __fs decHeadingVariant |> Result.bind (fun variant ->
    Ok { Level = level; Text = text; Variant = variant }))))

and private decBadgeSpec (j: JVal) : Result<BadgeSpec, string> =
    dObj j |> Result.bind (fun __fs ->
    dReq "label" __fs decTextSource |> Result.bind (fun label ->
    dReq "variant" __fs decBadgeVariant |> Result.bind (fun variant ->
    Ok { Label = label; Variant = variant })))

and private decMarkdownSpec (j: JVal) : Result<MarkdownSpec, string> =
    dObj j |> Result.bind (fun __fs ->
    dReq "text" __fs decTextSource |> Result.bind (fun text ->
    Ok { Text = text }))

and private decMathSpec (j: JVal) : Result<MathSpec, string> =
    dObj j |> Result.bind (fun __fs ->
    dReq "source" __fs dStr |> Result.bind (fun source ->
    dReq "display" __fs decMathDisplay |> Result.bind (fun display ->
    Ok { Source = source; Display = display })))

and private decSkeletonSpec (j: JVal) : Result<SkeletonSpec, string> =
    dObj j |> Result.bind (fun __fs ->
    dReq "rows" __fs dInt |> Result.bind (fun rows ->
    Ok { Rows = rows }))

and private decListSpec (j: JVal) : Result<ListSpec, string> =
    dObj j |> Result.bind (fun __fs ->
    dReq "items" __fs (dList decTextSource) |> Result.bind (fun items ->
    dReq "ordered" __fs dBool |> Result.bind (fun ordered ->
    Ok { Items = items; Ordered = ordered })))

and private decImageSpec (j: JVal) : Result<ImageSpec, string> =
    dObj j |> Result.bind (fun __fs ->
    dReq "alt" __fs decTextSource |> Result.bind (fun alt ->
    dReq "src" __fs (decBinding dStr) |> Result.bind (fun src ->
    dReq "variant" __fs decImageVariant |> Result.bind (fun variant ->
    Ok { Alt = alt; Src = src; Variant = variant }))))

and private decLinkSpec (j: JVal) : Result<LinkSpec, string> =
    dObj j |> Result.bind (fun __fs ->
    dReq "href" __fs (decBinding dStr) |> Result.bind (fun href ->
    dReq "label" __fs decTextSource |> Result.bind (fun label ->
    dReq "download" __fs dBool |> Result.bind (fun download ->
    dOpt "rel" __fs dStr |> Result.bind (fun rel ->
    dOpt "target" __fs dStr |> Result.bind (fun target ->
    Ok { Href = href; Label = label; Download = download; Rel = rel; Target = target }))))))

and private decCalloutSpec (j: JVal) : Result<CalloutSpec, string> =
    dObj j |> Result.bind (fun __fs ->
    dReq "body" __fs decTextSource |> Result.bind (fun body ->
    dDef "dismissable" __fs dBool (false) |> Result.bind (fun dismissable ->
    dDef "tone" __fs decToneVariant (ToneVariant.Default) |> Result.bind (fun tone ->
    dOpt "heading" __fs decTextSource |> Result.bind (fun heading ->
    dOpt "icon" __fs dStr |> Result.bind (fun icon ->
    Ok { Body = body; Dismissable = dismissable; Tone = tone; Heading = heading; Icon = icon }))))))

and private decProgressSpec (j: JVal) : Result<ProgressSpec, string> =
    dObj j |> Result.bind (fun __fs ->
    dReq "fraction" __fs (decBinding dFloat) |> Result.bind (fun fraction ->
    dDef "indeterminate" __fs dBool (false) |> Result.bind (fun indeterminate ->
    dDef "tone" __fs decToneVariant (ToneVariant.Default) |> Result.bind (fun tone ->
    dOpt "label" __fs decTextSource |> Result.bind (fun label ->
    dOpt "caveat" __fs decTextSource |> Result.bind (fun caveat ->
    Ok { Fraction = fraction; Indeterminate = indeterminate; Tone = tone; Label = label; Caveat = caveat }))))))

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
    dOpt "icon" __fs dStr |> Result.bind (fun icon ->
    dOpt "subtext" __fs decTextSource |> Result.bind (fun subtext ->
    Ok { Label = label; Value = value; Format = format; Tone = tone; Weight = weight; Emphasis = emphasis; Trend = trend; TrendFormat = trendFormat; Icon = icon; Subtext = subtext })))))))))))

and private decLabelValueRowSpec (j: JVal) : Result<LabelValueRowSpec, string> =
    dObj j |> Result.bind (fun __fs ->
    dDef "emphasis" __fs dBool (false) |> Result.bind (fun emphasis ->
    dDef "format" __fs decCellFormat (CellFormat.None) |> Result.bind (fun format ->
    dReq "label" __fs decTextSource |> Result.bind (fun label ->
    dReq "value" __fs (decBinding dFloat) |> Result.bind (fun value ->
    dOpt "help" __fs decTextSource |> Result.bind (fun help ->
    Ok { Emphasis = emphasis; Format = format; Label = label; Value = value; Help = help }))))))

and private decFactSpec (j: JVal) : Result<FactSpec, string> =
    dObj j |> Result.bind (fun __fs ->
    dDef "emphasis" __fs dBool (false) |> Result.bind (fun emphasis ->
    dOpt "help" __fs decTextSource |> Result.bind (fun help ->
    dOpt "icon" __fs dStr |> Result.bind (fun icon ->
    dReq "label" __fs decTextSource |> Result.bind (fun label ->
    dDef "tone" __fs decToneVariant (ToneVariant.Default) |> Result.bind (fun tone ->
    dReq "value" __fs decTextSource |> Result.bind (fun value ->
    Ok { Emphasis = emphasis; Help = help; Icon = icon; Label = label; Tone = tone; Value = value })))))))

and private decSparklineSpec (j: JVal) : Result<SparklineSpec, string> =
    dObj j |> Result.bind (fun __fs ->
    dReq "source" __fs (decBinding (dList dFloat)) |> Result.bind (fun source ->
    Ok { Source = source }))

and private decCodeBlockSpec (j: JVal) : Result<CodeBlockSpec, string> =
    dObj j |> Result.bind (fun __fs ->
    dReq "code" __fs dStr |> Result.bind (fun code ->
    dReq "copyable" __fs dBool |> Result.bind (fun copyable ->
    dReq "highlightLines" __fs (dList dInt) |> Result.bind (fun highlightLines ->
    dReq "language" __fs dStr |> Result.bind (fun language ->
    dReq "lineNumbers" __fs dBool |> Result.bind (fun lineNumbers ->
    Ok { Code = code; Copyable = copyable; HighlightLines = highlightLines; Language = language; LineNumbers = lineNumbers }))))))

and private decToastSpec (j: JVal) : Result<ToastSpec, string> =
    dObj j |> Result.bind (fun __fs ->
    dDef "dismissable" __fs dBool (true) |> Result.bind (fun dismissable ->
    dReq "message" __fs decTextSource |> Result.bind (fun message ->
    dReq "open" __fs (decBinding dBool) |> Result.bind (fun ``open`` ->
    dDef "tone" __fs decToneVariant (ToneVariant.Default) |> Result.bind (fun tone ->
    Ok { Dismissable = dismissable; Message = message; Open = ``open``; Tone = tone })))))

and private decDrawingSpec (j: JVal) : Result<DrawingSpec, string> =
    dObj j |> Result.bind (fun __fs ->
    dOpt "description" __fs decTextSource |> Result.bind (fun description ->
    dReq "shapes" __fs (dList decShape) |> Result.bind (fun shapes ->
    dReq "style" __fs decDrawStyle |> Result.bind (fun style ->
    dOpt "title" __fs decTextSource |> Result.bind (fun title ->
    dReq "viewBox" __fs decViewBox |> Result.bind (fun viewBox ->
    Ok { Description = description; Shapes = shapes; Style = style; Title = title; ViewBox = viewBox }))))))

and private decBoxSpec (j: JVal) : Result<BoxSpec<obj>, string> =
    dObj j |> Result.bind (fun __fs ->
    dReq "children" __fs (dList decNode) |> Result.bind (fun children ->
    dOpt "heading" __fs decTextSource |> Result.bind (fun heading ->
    dReq "layout" __fs decLayoutMode |> Result.bind (fun layout ->
    dReq "role" __fs decBoxRole |> Result.bind (fun role ->
    Ok { Children = children; Heading = heading; Layout = layout; Role = role })))))

and private decSplitPanelSpec (j: JVal) : Result<SplitPanelSpec<obj>, string> =
    dObj j |> Result.bind (fun __fs ->
    dReq "children" __fs (dList decNode) |> Result.bind (fun children ->
    dReq "weight" __fs dFloat |> Result.bind (fun weight ->
    Ok { Children = children; Weight = weight })))

and private decSummaryListSpec (j: JVal) : Result<SummaryListSpec<obj>, string> =
    dObj j |> Result.bind (fun __fs ->
    dReq "children" __fs (dList decNode) |> Result.bind (fun children ->
    dOpt "heading" __fs decTextSource |> Result.bind (fun heading ->
    Ok { Children = children; Heading = heading })))

and private decDisclosureSpec (j: JVal) : Result<DisclosureSpec<obj>, string> =
    dObj j |> Result.bind (fun __fs ->
    dReq "children" __fs (dList decNode) |> Result.bind (fun children ->
    dReq "defaultOpen" __fs dBool |> Result.bind (fun defaultOpen ->
    dReq "heading" __fs decTextSource |> Result.bind (fun heading ->
    (dPresent "onToggle" __fs |> Result.map (Option.map (fun () -> (fun (_: bool) -> Action.Chain [])))) |> Result.bind (fun onToggle ->
    dReq "open" __fs (decBinding dBool) |> Result.bind (fun ``open`` ->
    Ok { Children = children; DefaultOpen = defaultOpen; Heading = heading; OnToggle = onToggle; Open = ``open`` }))))))

and private decModalSpec (j: JVal) : Result<ModalSpec<obj>, string> =
    dObj j |> Result.bind (fun __fs ->
    dReq "children" __fs (dList decNode) |> Result.bind (fun children ->
    dReq "dismissable" __fs dBool |> Result.bind (fun dismissable ->
    dOpt "onDismiss" __fs decAction |> Result.bind (fun onDismiss ->
    dReq "open" __fs (decBinding dBool) |> Result.bind (fun ``open`` ->
    dOpt "heading" __fs decTextSource |> Result.bind (fun heading ->
    Ok { Children = children; Dismissable = dismissable; OnDismiss = onDismiss; Open = ``open``; Heading = heading }))))))

and private decScrollAreaSpec (j: JVal) : Result<ScrollAreaSpec<obj>, string> =
    dObj j |> Result.bind (fun __fs ->
    dReq "children" __fs (dList decNode) |> Result.bind (fun children ->
    dReq "orientation" __fs decScrollOrientation |> Result.bind (fun orientation ->
    dOpt "maxHeight" __fs dInt |> Result.bind (fun maxHeight ->
    dOpt "maxWidth" __fs dInt |> Result.bind (fun maxWidth ->
    Ok { Children = children; Orientation = orientation; MaxHeight = maxHeight; MaxWidth = maxWidth })))))

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

and private decStepperSpec (j: JVal) : Result<StepperSpec<obj>, string> =
    dObj j |> Result.bind (fun __fs ->
    dReq "activeStep" __fs (decBinding dInt) |> Result.bind (fun activeStep ->
    dReq "children" __fs (dList decNode) |> Result.bind (fun children ->
    (dPresent "onSelect" __fs |> Result.map (Option.map (fun () -> (fun (_: int) -> Action.Chain [])))) |> Result.bind (fun onSelect ->
    Ok { ActiveStep = activeStep; Children = children; OnSelect = onSelect }))))

and private decButtonSpec (j: JVal) : Result<ButtonSpec<obj>, string> =
    dObj j |> Result.bind (fun __fs ->
    dReq "label" __fs decTextSource |> Result.bind (fun label ->
    dReq "onClick" __fs decAction |> Result.bind (fun onClick ->
    dReq "variant" __fs decButtonVariant |> Result.bind (fun variant ->
    dOpt "icon" __fs dStr |> Result.bind (fun icon ->
    Ok (None) |> Result.bind (fun tooltip ->
    dOpt "disabled" __fs (decBinding dBool) |> Result.bind (fun disabled ->
    Ok { Label = label; OnClick = onClick; Variant = variant; Icon = icon; Tooltip = tooltip; Disabled = disabled })))))))

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

and private decFileUploadSpec (j: JVal) : Result<FileUploadSpec<obj>, string> =
    dObj j |> Result.bind (fun __fs ->
    dReq "accept" __fs (dList dStr) |> Result.bind (fun accept ->
    dReq "label" __fs decTextSource |> Result.bind (fun label ->
    dReq "multiple" __fs dBool |> Result.bind (fun multiple ->
    (dPresent "onSelect" __fs |> Result.map (Option.map (fun () -> (fun (_: Fuaran.UI.HostPrelude.FileSelection list) -> Action.Chain [])))) |> Result.bind (fun onSelect ->
    dOpt "disabled" __fs (decBinding dBool) |> Result.bind (fun disabled ->
    Ok { Accept = accept; Label = label; Multiple = multiple; OnSelect = onSelect; Disabled = disabled }))))))

and private decFormSpec (j: JVal) : Result<FormSpec<obj>, string> =
    dObj j |> Result.bind (fun __fs ->
    dReq "fields" __fs (dList decFormField) |> Result.bind (fun fields ->
    dReq "onSubmit" __fs decAction |> Result.bind (fun onSubmit ->
    dReq "submitLabel" __fs decTextSource |> Result.bind (fun submitLabel ->
    dOpt "disabled" __fs (decBinding dBool) |> Result.bind (fun disabled ->
    Ok { Fields = fields; OnSubmit = onSubmit; SubmitLabel = submitLabel; Disabled = disabled })))))

and private decFiltersSpec (j: JVal) : Result<FiltersSpec<obj>, string> =
    dObj j |> Result.bind (fun __fs ->
    dReq "items" __fs (dList decFilterSpec) |> Result.bind (fun items ->
    Ok { Items = items }))

and private decDataGridSpec (j: JVal) : Result<DataGridSpec<obj>, string> =
    dObj j |> Result.bind (fun __fs ->
    dReq "columns" __fs (dList decColumnErased) |> Result.bind (fun columns ->
    dDef "editable" __fs dBool (false) |> Result.bind (fun editable ->
    (dPresent "rowKey" __fs |> Result.map (Option.map (fun () -> (fun _ -> "")))) |> Result.bind (fun rowKey ->
    dOpt "rowKeyField" __fs dStr |> Result.bind (fun rowKeyField ->
    dReq "source" __fs (decBinding (fun _ -> Ok(Seq.empty: obj seq))) |> Result.bind (fun source ->
    dOpt "staticRows" __fs decStaticRows |> Result.bind (fun staticRows ->
    (dPresent "onRowClick" __fs |> Result.map (Option.map (fun () -> (fun (_: obj) -> Action.Chain [])))) |> Result.bind (fun onRowClick ->
    Ok { Columns = columns; Editable = editable; RowKey = rowKey; RowKeyField = rowKeyField; Source = source; StaticRows = staticRows; OnRowClick = onRowClick }))))))))

and private decChartSpec (j: JVal) : Result<ChartSpec<obj>, string> =
    dObj j |> Result.bind (fun __fs ->
    dReq "kind" __fs decChartKind |> Result.bind (fun kind ->
    dReq "source" __fs (decBinding (fun _ -> Ok(Seq.empty: obj seq))) |> Result.bind (fun source ->
    dReq "stacked" __fs dBool |> Result.bind (fun stacked ->
    dReq "xField" __fs dStr |> Result.bind (fun xField ->
    dReq "yFields" __fs (dList dStr) |> Result.bind (fun yFields ->
    dOpt "title" __fs decTextSource |> Result.bind (fun title ->
    (dPresent "onPointClick" __fs |> Result.map (Option.map (fun () -> (fun (_: obj) -> Action.Chain [])))) |> Result.bind (fun onPointClick ->
    Ok { Kind = kind; Source = source; Stacked = stacked; XField = xField; YFields = yFields; Title = title; OnPointClick = onPointClick }))))))))

and private decMapSpec (j: JVal) : Result<MapSpec<obj>, string> =
    dObj j |> Result.bind (fun __fs ->
    dReq "centreLatitude" __fs dFloat |> Result.bind (fun centreLatitude ->
    dReq "centreLongitude" __fs dFloat |> Result.bind (fun centreLongitude ->
    dReq "source" __fs (decBinding (dList decMapMarker)) |> Result.bind (fun source ->
    dReq "zoom" __fs dInt |> Result.bind (fun zoom ->
    (dPresent "onMarkerClick" __fs |> Result.map (Option.map (fun () -> (fun (_: MapMarker) -> Action.Chain [])))) |> Result.bind (fun onMarkerClick ->
    Ok { CentreLatitude = centreLatitude; CentreLongitude = centreLongitude; Source = source; Zoom = zoom; OnMarkerClick = onMarkerClick }))))))

and private decCustomSpec (j: JVal) : Result<CustomSpec, string> =
    dObj j |> Result.bind (fun __fs ->
    dReq "moduleId" __fs dStr |> Result.bind (fun moduleId ->
    dReq "componentId" __fs dStr |> Result.bind (fun componentId ->
    dReq "props" __fs (dMap dJson) |> Result.bind (fun props ->
    dOpt "contentHash" __fs decContentHash |> Result.bind (fun contentHash ->
    dOpt "exposedNodeIds" __fs (dList dStr) |> Result.bind (fun exposedNodeIds ->
    Ok { ModuleId = moduleId; ComponentId = componentId; Props = props; ContentHash = contentHash; ExposedNodeIds = exposedNodeIds }))))))

and private decErrorBoundarySpec (j: JVal) : Result<ErrorBoundarySpec<obj>, string> =
    dObj j |> Result.bind (fun __fs ->
    dReq "child" __fs decNode |> Result.bind (fun child ->
    dReq "fallback" __fs decNode |> Result.bind (fun fallback ->
    Ok { Child = child; Fallback = fallback })))

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

and private decSwitchSpec (j: JVal) : Result<SwitchSpec<obj>, string> =
    dObj j |> Result.bind (fun __fs ->
    dReq "cases" __fs (dList decSwitchCase) |> Result.bind (fun cases ->
    dReq "default" __fs decNode |> Result.bind (fun ``default`` ->
    dReq "stateKey" __fs dStr |> Result.bind (fun stateKey ->
    Ok { Cases = cases; Default = ``default``; StateKey = stateKey }))))

and private decMountSpec (j: JVal) : Result<MountSpec<obj>, string> =
    dObj j |> Result.bind (fun __fs ->
    dReq "capabilities" __fs (dList dStr) |> Result.bind (fun capabilities ->
    dReq "channel" __fs decGuestChannel |> Result.bind (fun channel ->
    dOpt "inputs" __fs (dMap decFragmentArg) |> Result.bind (fun inputs ->
    (dPresent "onBubble" __fs |> Result.map (Option.map (fun () -> (fun (_: obj) -> Action.Chain [])))) |> Result.bind (fun onBubble ->
    dReq "scopeId" __fs dStr |> Result.bind (fun scopeId ->
    Ok { Capabilities = capabilities; Channel = channel; Inputs = inputs; OnBubble = onBubble; ScopeId = scopeId }))))))

/// Structural decode. The policy layer (diagnostics, §16 lenient-accept,
/// the reject set) composes ABOVE this — see the Phase 672 note in the generator.
let decodeNode (s: string) : Result<Node<obj>, string> =
    Json.parse s |> Result.bind decNode

let private witnessKindTag (n: Node<'Msg>) : string =
    match n.Kind with
    | NodeKind.Heading _ -> "Heading"
    | NodeKind.Badge _ -> "Badge"
    | NodeKind.Markdown _ -> "Markdown"
    | NodeKind.Math _ -> "Math"
    | NodeKind.Skeleton _ -> "Skeleton"
    | NodeKind.List _ -> "List"
    | NodeKind.Image _ -> "Image"
    | NodeKind.Link _ -> "Link"
    | NodeKind.Callout _ -> "Callout"
    | NodeKind.Progress _ -> "Progress"
    | NodeKind.Metric _ -> "Metric"
    | NodeKind.LabelValueRow _ -> "LabelValueRow"
    | NodeKind.Fact _ -> "Fact"
    | NodeKind.Sparkline _ -> "Sparkline"
    | NodeKind.CodeBlock _ -> "CodeBlock"
    | NodeKind.Toast _ -> "Toast"
    | NodeKind.Drawing _ -> "Drawing"
    | NodeKind.Box _ -> "Box"
    | NodeKind.SplitPanel _ -> "SplitPanel"
    | NodeKind.SummaryList _ -> "SummaryList"
    | NodeKind.Disclosure _ -> "Disclosure"
    | NodeKind.Modal _ -> "Modal"
    | NodeKind.ScrollArea _ -> "ScrollArea"
    | NodeKind.Tabs _ -> "Tabs"
    | NodeKind.Stepper _ -> "Stepper"
    | NodeKind.Button _ -> "Button"
    | NodeKind.Select _ -> "Select"
    | NodeKind.FileUpload _ -> "FileUpload"
    | NodeKind.Form _ -> "Form"
    | NodeKind.Filters _ -> "Filters"
    | NodeKind.DataGrid _ -> "DataGrid"
    | NodeKind.Chart _ -> "Chart"
    | NodeKind.Map _ -> "Map"
    | NodeKind.Custom _ -> "Custom"
    | NodeKind.ErrorBoundary _ -> "ErrorBoundary"
    | NodeKind.FragmentDecl _ -> "FragmentDecl"
    | NodeKind.FragmentRef _ -> "FragmentRef"
    | NodeKind.Switch _ -> "Switch"
    | NodeKind.Mount _ -> "Mount"

let private witnessChildren (n: Node<'Msg>) : Node<'Msg> list =
    match n.Kind with
    | NodeKind.Box s -> s.Children
    | NodeKind.SplitPanel s -> s.Children
    | NodeKind.SummaryList s -> s.Children
    | NodeKind.Disclosure s -> s.Children
    | NodeKind.Modal s -> s.Children
    | NodeKind.ScrollArea s -> s.Children
    | NodeKind.Tabs s -> s.Children
    | NodeKind.Stepper s -> s.Children
    | NodeKind.ErrorBoundary s -> [ s.Child ] @ [ s.Fallback ]
    | NodeKind.FragmentDecl s -> [ s.Body ]
    | NodeKind.Switch s -> [ s.Default ]
    | _ -> []

let private witnessReplaceChildren (n: Node<'Msg>) (kids: Node<'Msg> list) : Node<'Msg> =
    match n.Kind with
    | NodeKind.Box s -> { n with Kind = NodeKind.Box { s with Children = kids } }
    | NodeKind.SplitPanel s -> { n with Kind = NodeKind.SplitPanel { s with Children = kids } }
    | NodeKind.SummaryList s -> { n with Kind = NodeKind.SummaryList { s with Children = kids } }
    | NodeKind.Disclosure s -> { n with Kind = NodeKind.Disclosure { s with Children = kids } }
    | NodeKind.Modal s -> { n with Kind = NodeKind.Modal { s with Children = kids } }
    | NodeKind.ScrollArea s -> { n with Kind = NodeKind.ScrollArea { s with Children = kids } }
    | NodeKind.Tabs s -> { n with Kind = NodeKind.Tabs { s with Children = kids } }
    | NodeKind.Stepper s -> { n with Kind = NodeKind.Stepper { s with Children = kids } }
    | NodeKind.ErrorBoundary s -> { n with Kind = NodeKind.ErrorBoundary { s with Child = List.item 0 kids; Fallback = List.item 1 kids } }
    | NodeKind.FragmentDecl s -> { n with Kind = NodeKind.FragmentDecl { s with Body = List.head kids } }
    | NodeKind.Switch s -> { n with Kind = NodeKind.Switch { s with Default = List.head kids } }
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

let mkHeading (id: string) (level: int) (text: TextSource) (variant: HeadingVariant) : Node<'Msg> =
    { Id = id; Kind = NodeKind.Heading { Level = level; Text = text; Variant = variant }; Accessibility = None; ExtraAttributes = None; Motion = None; State = None; Style = None }

let mkBadge (id: string) (label: TextSource) (variant: BadgeVariant) : Node<'Msg> =
    { Id = id; Kind = NodeKind.Badge { Label = label; Variant = variant }; Accessibility = None; ExtraAttributes = None; Motion = None; State = None; Style = None }

let mkMarkdown (id: string) (text: TextSource) : Node<'Msg> =
    { Id = id; Kind = NodeKind.Markdown { Text = text }; Accessibility = None; ExtraAttributes = None; Motion = None; State = None; Style = None }

let mkMath (id: string) (source: string) (display: MathDisplay) : Node<'Msg> =
    { Id = id; Kind = NodeKind.Math { Source = source; Display = display }; Accessibility = None; ExtraAttributes = None; Motion = None; State = None; Style = None }

let mkSkeleton (id: string) (rows: int) : Node<'Msg> =
    { Id = id; Kind = NodeKind.Skeleton { Rows = rows }; Accessibility = None; ExtraAttributes = None; Motion = None; State = None; Style = None }

let mkList (id: string) (items: TextSource list) (ordered: bool) : Node<'Msg> =
    { Id = id; Kind = NodeKind.List { Items = items; Ordered = ordered }; Accessibility = None; ExtraAttributes = None; Motion = None; State = None; Style = None }

let mkImage (id: string) (alt: TextSource) (src: Binding<string>) (variant: ImageVariant) : Node<'Msg> =
    { Id = id; Kind = NodeKind.Image { Alt = alt; Src = src; Variant = variant }; Accessibility = None; ExtraAttributes = None; Motion = None; State = None; Style = None }

let mkLink (id: string) (href: Binding<string>) (label: TextSource) (download: bool) : Node<'Msg> =
    { Id = id; Kind = NodeKind.Link { Href = href; Label = label; Download = download; Rel = None; Target = None }; Accessibility = None; ExtraAttributes = None; Motion = None; State = None; Style = None }

let mkCallout (id: string) (body: TextSource) : Node<'Msg> =
    { Id = id; Kind = NodeKind.Callout { Body = body; Dismissable = false; Tone = ToneVariant.Default; Heading = None; Icon = None }; Accessibility = None; ExtraAttributes = None; Motion = None; State = None; Style = None }

let mkProgress (id: string) (fraction: Binding<float>) : Node<'Msg> =
    { Id = id; Kind = NodeKind.Progress { Fraction = fraction; Indeterminate = false; Tone = ToneVariant.Default; Label = None; Caveat = None }; Accessibility = None; ExtraAttributes = None; Motion = None; State = None; Style = None }

let mkMetric (id: string) (label: TextSource) (value: Binding<float>) : Node<'Msg> =
    { Id = id; Kind = NodeKind.Metric { Label = label; Value = value; Format = CellFormat.None; Tone = ToneVariant.Default; Weight = StyleWeight.Standard; Emphasis = Emphasis.Normal; Trend = None; TrendFormat = None; Icon = None; Subtext = None }; Accessibility = None; ExtraAttributes = None; Motion = None; State = None; Style = None }

let mkLabelValueRow (id: string) (label: TextSource) (value: Binding<float>) : Node<'Msg> =
    { Id = id; Kind = NodeKind.LabelValueRow { Emphasis = false; Format = CellFormat.None; Label = label; Value = value; Help = None }; Accessibility = None; ExtraAttributes = None; Motion = None; State = None; Style = None }

let mkFact (id: string) (label: TextSource) (value: TextSource) : Node<'Msg> =
    { Id = id; Kind = NodeKind.Fact { Emphasis = false; Help = None; Icon = None; Label = label; Tone = ToneVariant.Default; Value = value }; Accessibility = None; ExtraAttributes = None; Motion = None; State = None; Style = None }

let mkSparkline (id: string) (source: Binding<float list>) : Node<'Msg> =
    { Id = id; Kind = NodeKind.Sparkline { Source = source }; Accessibility = None; ExtraAttributes = None; Motion = None; State = None; Style = None }

let mkCodeBlock (id: string) (code: string) (copyable: bool) (highlightLines: int list) (language: string) (lineNumbers: bool) : Node<'Msg> =
    { Id = id; Kind = NodeKind.CodeBlock { Code = code; Copyable = copyable; HighlightLines = highlightLines; Language = language; LineNumbers = lineNumbers }; Accessibility = None; ExtraAttributes = None; Motion = None; State = None; Style = None }

let mkToast (id: string) (message: TextSource) (``open``: Binding<bool>) : Node<'Msg> =
    { Id = id; Kind = NodeKind.Toast { Dismissable = true; Message = message; Open = ``open``; Tone = ToneVariant.Default }; Accessibility = None; ExtraAttributes = None; Motion = None; State = None; Style = None }

let mkDrawing (id: string) (shapes: Shape list) (style: DrawStyle) (viewBox: ViewBox) : Node<'Msg> =
    { Id = id; Kind = NodeKind.Drawing { Description = None; Shapes = shapes; Style = style; Title = None; ViewBox = viewBox }; Accessibility = None; ExtraAttributes = None; Motion = None; State = None; Style = None }

let mkBox (id: string) (children: Node<'Msg> list) (layout: LayoutMode) (role: BoxRole) : Node<'Msg> =
    { Id = id; Kind = NodeKind.Box { Children = children; Heading = None; Layout = layout; Role = role }; Accessibility = None; ExtraAttributes = None; Motion = None; State = None; Style = None }

let mkSplitPanel (id: string) (children: Node<'Msg> list) (weight: float) : Node<'Msg> =
    { Id = id; Kind = NodeKind.SplitPanel { Children = children; Weight = weight }; Accessibility = None; ExtraAttributes = None; Motion = None; State = None; Style = None }

let mkSummaryList (id: string) (children: Node<'Msg> list) : Node<'Msg> =
    { Id = id; Kind = NodeKind.SummaryList { Children = children; Heading = None }; Accessibility = None; ExtraAttributes = None; Motion = None; State = None; Style = None }

let mkDisclosure (id: string) (children: Node<'Msg> list) (defaultOpen: bool) (heading: TextSource) (``open``: Binding<bool>) : Node<'Msg> =
    { Id = id; Kind = NodeKind.Disclosure { Children = children; DefaultOpen = defaultOpen; Heading = heading; OnToggle = None; Open = ``open`` }; Accessibility = None; ExtraAttributes = None; Motion = None; State = None; Style = None }

let mkModal (id: string) (children: Node<'Msg> list) (dismissable: bool) (``open``: Binding<bool>) : Node<'Msg> =
    { Id = id; Kind = NodeKind.Modal { Children = children; Dismissable = dismissable; OnDismiss = None; Open = ``open``; Heading = None }; Accessibility = None; ExtraAttributes = None; Motion = None; State = None; Style = None }

let mkScrollArea (id: string) (children: Node<'Msg> list) (orientation: ScrollOrientation) : Node<'Msg> =
    { Id = id; Kind = NodeKind.ScrollArea { Children = children; Orientation = orientation; MaxHeight = None; MaxWidth = None }; Accessibility = None; ExtraAttributes = None; Motion = None; State = None; Style = None }

let mkTabs (id: string) (activeIndex: Binding<int>) (children: Node<'Msg> list) : Node<'Msg> =
    { Id = id; Kind = NodeKind.Tabs { ActiveIndex = activeIndex; Children = children; Orientation = Orientation.Horizontal; OnSelect = None; OnSelectTag = None; TabHeaders = None; TabTags = None; ActiveTag = None }; Accessibility = None; ExtraAttributes = None; Motion = None; State = None; Style = None }

let mkStepper (id: string) (activeStep: Binding<int>) (children: Node<'Msg> list) : Node<'Msg> =
    { Id = id; Kind = NodeKind.Stepper { ActiveStep = activeStep; Children = children; OnSelect = None }; Accessibility = None; ExtraAttributes = None; Motion = None; State = None; Style = None }

let mkButton (id: string) (label: TextSource) (onClick: Action<'Msg>) (variant: ButtonVariant) : Node<'Msg> =
    { Id = id; Kind = NodeKind.Button { Label = label; OnClick = onClick; Variant = variant; Icon = None; Tooltip = None; Disabled = None }; Accessibility = None; ExtraAttributes = None; Motion = None; State = None; Style = None }

let mkSelect (id: string) (label: TextSource) (source: Binding<SelectOption list>) (value: Binding<string>) : Node<'Msg> =
    { Id = id; Kind = NodeKind.Select { Label = label; OnChange = None; OnChangeMulti = None; Source = source; Value = value; Placeholder = None; Disabled = None; Multiple = None; Values = None }; Accessibility = None; ExtraAttributes = None; Motion = None; State = None; Style = None }

let mkFileUpload (id: string) (accept: string list) (label: TextSource) (multiple: bool) : Node<'Msg> =
    { Id = id; Kind = NodeKind.FileUpload { Accept = accept; Label = label; Multiple = multiple; OnSelect = None; Disabled = None }; Accessibility = None; ExtraAttributes = None; Motion = None; State = None; Style = None }

let mkForm (id: string) (fields: FormField<'Msg> list) (onSubmit: Action<'Msg>) (submitLabel: TextSource) : Node<'Msg> =
    { Id = id; Kind = NodeKind.Form { Fields = fields; OnSubmit = onSubmit; SubmitLabel = submitLabel; Disabled = None }; Accessibility = None; ExtraAttributes = None; Motion = None; State = None; Style = None }

let mkFilters (id: string) (items: FilterSpec<'Msg> list) : Node<'Msg> =
    { Id = id; Kind = NodeKind.Filters { Items = items }; Accessibility = None; ExtraAttributes = None; Motion = None; State = None; Style = None }

let mkDataGrid (id: string) (columns: ColumnErased<'Msg> list) (source: Binding<obj seq>) : Node<'Msg> =
    { Id = id; Kind = NodeKind.DataGrid { Columns = columns; Editable = false; RowKey = None; RowKeyField = None; Source = source; StaticRows = None; OnRowClick = None }; Accessibility = None; ExtraAttributes = None; Motion = None; State = None; Style = None }

let mkChart (id: string) (kind: ChartKind) (source: Binding<obj seq>) (stacked: bool) (xField: string) (yFields: string list) : Node<'Msg> =
    { Id = id; Kind = NodeKind.Chart { Kind = kind; Source = source; Stacked = stacked; XField = xField; YFields = yFields; Title = None; OnPointClick = None }; Accessibility = None; ExtraAttributes = None; Motion = None; State = None; Style = None }

let mkMap (id: string) (centreLatitude: float) (centreLongitude: float) (source: Binding<MapMarker list>) (zoom: int) : Node<'Msg> =
    { Id = id; Kind = NodeKind.Map { CentreLatitude = centreLatitude; CentreLongitude = centreLongitude; Source = source; Zoom = zoom; OnMarkerClick = None }; Accessibility = None; ExtraAttributes = None; Motion = None; State = None; Style = None }

let mkCustom (id: string) (moduleId: string) (componentId: string) (props: Map<string, JVal>) : Node<'Msg> =
    { Id = id; Kind = NodeKind.Custom { ModuleId = moduleId; ComponentId = componentId; Props = props; ContentHash = None; ExposedNodeIds = None }; Accessibility = None; ExtraAttributes = None; Motion = None; State = None; Style = None }

let mkErrorBoundary (id: string) (child: Node<'Msg>) (fallback: Node<'Msg>) : Node<'Msg> =
    { Id = id; Kind = NodeKind.ErrorBoundary { Child = child; Fallback = fallback }; Accessibility = None; ExtraAttributes = None; Motion = None; State = None; Style = None }

let mkFragmentDecl (id: string) (body: Node<'Msg>) (name: string) : Node<'Msg> =
    { Id = id; Kind = NodeKind.FragmentDecl { Body = body; Name = name; Holes = None; Effect = None }; Accessibility = None; ExtraAttributes = None; Motion = None; State = None; Style = None }

let mkFragmentRef (id: string) (name: string) : Node<'Msg> =
    { Id = id; Kind = NodeKind.FragmentRef { Name = name; Args = None }; Accessibility = None; ExtraAttributes = None; Motion = None; State = None; Style = None }

let mkSwitch (id: string) (cases: SwitchCase<'Msg> list) (``default``: Node<'Msg>) (stateKey: string) : Node<'Msg> =
    { Id = id; Kind = NodeKind.Switch { Cases = cases; Default = ``default``; StateKey = stateKey }; Accessibility = None; ExtraAttributes = None; Motion = None; State = None; Style = None }

let mkMount (id: string) (capabilities: string list) (channel: GuestChannel) (scopeId: string) : Node<'Msg> =
    { Id = id; Kind = NodeKind.Mount { Capabilities = capabilities; Channel = channel; Inputs = None; OnBubble = None; ScopeId = scopeId }; Accessibility = None; ExtraAttributes = None; Motion = None; State = None; Style = None }