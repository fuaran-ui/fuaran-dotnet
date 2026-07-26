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
type TextSource =
    | Literal of text: string
    | Bound of binding: Binding<string>

and [<RequireQualifiedAccess>] Binding<'T> =
    | Static of value: 'T option
    | Query of dependsOn: string list option * name: string
    | Filter of name: string
    | State of defaultValue: 'T option * key: string
    | Computed of fn: unit
    | Local of flushOn: LocalFlushTrigger * format: unit * initialFrom: Binding<'T> * onCommit: unit option * parse: unit
    | Format of format: Format * locale: LocaleSource * source: Binding<float>
    | Invoke of args: InvokeArg list * capabilityId: string

and [<RequireQualifiedAccess>] CellFormat =
    | None
    | Number of decimals: int option
    | Currency of code: string
    | Percent of decimals: int option
    | SignificantDigits of digits: int
    | Date of format: string
    | Custom of fn: unit

and [<RequireQualifiedAccess>] Action =
    | Chain of ops: Action list
    | WriteToClipboard of text: string
    | Dispatch
    | Invoke of args: InvokeArg list * capabilityId: string
    | ReadFileBody of encoding: FileReadEncoding * fileRef: string * onRead: unit option
    | Call of endpoint: string * into: CallResultTarget option * onResult: unit option
    | Navigate of route: string
    | CommitLocal of nodeId: string
    | Notify of channel: string * payload: JVal
    | SetState of key: string * value: JVal
    | AiTool of args: JVal * toolName: string

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
    | Flex of direction: Orientation * wrap: bool
    | Grid of cols: int * templateColumns: string option

and [<RequireQualifiedAccess>] FormFieldKind =
    | Text of onChange: unit option * value: Binding<string>
    | Number of onChange: unit option * value: Binding<float>
    | Checkbox of onToggle: unit option * value: Binding<bool>
    | Choice of onChange: unit option * options: Binding<SelectOption list> * value: Binding<string>
    | TextArea of onChange: unit option * rows: int * value: Binding<string>
    | RangedNumber of onChange: unit option * value: Binding<float> * min: float option * max: float option * step: float option
    | SegmentedChoice of onChange: unit option * options: Binding<SelectOption list> * orientation: Orientation * value: Binding<string>
    | Date of onChange: unit option * value: Binding<string> * variant: DateVariant * min: string option * max: string option * step: float option

and [<RequireQualifiedAccess>] FilterKind =
    | Text of onChange: unit option * value: Binding<string>
    | Choice of onChange: unit option * options: Binding<SelectOption list> * value: Binding<string>
    | Range of onChange: unit option * value: unit
    | SegmentedChoice of onChange: unit option * options: Binding<SelectOption list> * orientation: Orientation * value: Binding<string>

and [<RequireQualifiedAccess>] ColumnWidth =
    | Auto
    | Fixed of pixels: int
    | Flex of weight: float

and [<RequireQualifiedAccess>] CellKindErased =
    | Text
    | Numeric
    | Date
    | Editable of onEdit: unit option
    | Checkbox of get: unit * onToggle: unit option
    | Button of label: TextSource * onClick: unit option
    | ButtonGroup of buttons: ButtonGroupItem list
    | Link of hrefFn: unit * labelFn: unit
    | Pill of labelFn: unit * toneFn: unit
    | Progress of fractionFn: unit * labelFn: unit
    | Custom of fn: unit

and [<RequireQualifiedAccess>] HoleValueSpace =
    | IntRange of max: int * min: int
    | FloatRange of max: float * min: float
    | StringLen of maxLen: int * minLen: int
    | Enum of choices: string list
    | AnyString

and [<RequireQualifiedAccess>] Scalar =
    | Int of value: int
    | Float of value: float
    | Bool of value: bool
    | Str of value: string

and [<RequireQualifiedAccess>] HoleDecl =
    | Value of ``default``: Scalar option * name: string * space: HoleValueSpace
    | Slot of kindConstraint: string option * name: string
    | Repeat of countSpace: HoleValueSpace * name: string

and [<RequireQualifiedAccess>] FragmentArg =
    | Int of value: int
    | Float of value: float
    | Bool of value: bool
    | Str of value: string
    | SlotArg of tree: Node

and [<RequireQualifiedAccess>] CurveCommand =
    | MoveTo of ``to``: DrawPoint
    | LineTo of ``to``: DrawPoint
    | CubicTo of control1: DrawPoint * control2: DrawPoint * ``to``: DrawPoint
    | QuadraticTo of control: DrawPoint * ``to``: DrawPoint
    | Close

and [<RequireQualifiedAccess>] Shape =
    | Group of children: Shape list * style: DrawStyle
    | Rectangle of cornerRadius: float option * height: float * style: DrawStyle * width: float * x: float * y: float
    | Line of style: DrawStyle * x1: float * x2: float * y1: float * y2: float
    | Polyline of points: DrawPoint list * style: DrawStyle
    | Polygon of points: DrawPoint list * style: DrawStyle
    | Curve of commands: CurveCommand list * style: DrawStyle
    | Circle of cx: float * cy: float * r: float * style: DrawStyle
    | Ellipse of cx: float * cy: float * rx: float * ry: float * style: DrawStyle
    | Label of style: DrawStyle * text: TextSource * x: float * y: float

and SwitchCase =
    {
      Child: Node
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
      Headers: string list
      Rows: string list list
    }

and FormField =
    {
      Id: string
      Kind: FormFieldKind
      Label: TextSource
      Required: bool
      Help: TextSource option
    }

and FilterSpec =
    {
      Kind: FilterKind
      Label: TextSource
      Name: string
    }

and TabHeader =
    {
      Label: TextSource
      Icon: string option
      Disabled: Binding<bool> option
    }

and ColumnErased =
    {
      Format: CellFormat
      Kind: CellKindErased
      Label: string
      Value: unit
      Width: ColumnWidth
    }

and ButtonGroupItem =
    {
      Label: TextSource
      OnClick: unit option
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
      Source: Binding<int list>
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
and BoxSpec =
    {
      Children: Node list
      Heading: TextSource option
      Layout: LayoutMode
      Role: BoxRole
    }

// Layout
and SplitPanelSpec =
    {
      Children: Node list
      Weight: float
    }

// Layout
and SummaryListSpec =
    {
      Children: Node list
      Heading: TextSource option
    }

// Layout
and DisclosureSpec =
    {
      Children: Node list
      DefaultOpen: bool
      Heading: TextSource
      OnToggle: unit option
      Open: Binding<bool>
    }

// Layout
and ModalSpec =
    {
      Children: Node list
      Dismissable: bool
      OnDismiss: Action
      Open: Binding<bool>
      Heading: TextSource option
    }

// Layout
and ScrollAreaSpec =
    {
      Children: Node list
      Orientation: ScrollOrientation
      MaxHeight: int option
      MaxWidth: int option
    }

// Layout
and TabsSpec =
    {
      ActiveIndex: Binding<int>
      Children: Node list
      OnSelect: unit option
      OnSelectTag: unit option
      TabHeaders: TabHeader list option
      TabTags: string list option
      ActiveTag: Binding<string> option
    }

// Layout
and StepperSpec =
    {
      ActiveStep: Binding<int>
      Children: Node list
      OnSelect: unit option
    }

// Input
and ButtonSpec =
    {
      Label: TextSource
      OnClick: Action
      Variant: ButtonVariant
      Icon: string option
      Tooltip: TextSource option
      Disabled: Binding<bool> option
    }

// Input
and SelectSpec =
    {
      Label: TextSource
      OnChange: unit option
      OnChangeMulti: unit option
      Source: Binding<SelectOption list>
      Value: Binding<string>
      Placeholder: TextSource option
      Disabled: Binding<bool> option
      Multiple: bool option
      Values: Binding<string list> option
    }

// Input
and FileUploadSpec =
    {
      Accept: string list
      Label: TextSource
      Multiple: bool
      OnSelect: unit option
      Disabled: Binding<bool> option
    }

// Input
and FormSpec =
    {
      Fields: FormField list
      OnSubmit: Action
      SubmitLabel: TextSource
      Disabled: Binding<bool> option
    }

// Input
and FiltersSpec =
    {
      Items: FilterSpec list
    }

// Visualisation
and DataGridSpec =
    {
      Columns: ColumnErased list
      Editable: bool
      RowKey: unit option
      Source: Binding<unit>
      StaticRows: StaticRows option
      OnRowClick: unit option
    }

// Visualisation
and ChartSpec =
    {
      Kind: ChartKind
      Source: Binding<unit>
      Stacked: bool
      XField: string
      YFields: string list
      Title: TextSource option
      OnPointClick: unit option
    }

// Visualisation
and MapSpec =
    {
      CentreLatitude: float
      CentreLongitude: float
      Source: Binding<MapMarker list>
      Zoom: int
      OnMarkerClick: unit option
    }

// Meta
and CustomSpec =
    {
      ModuleId: string
      ComponentId: string
      Props: Map<string, unit>
      ContentHash: ContentHash option
      ExposedNodeIds: string list option
    }

// Meta
and ErrorBoundarySpec =
    {
      Child: Node
      Fallback: Node
    }

// Meta
and FragmentDeclSpec =
    {
      Body: Node
      Name: string
      Holes: HoleDecl list option
      Effect: EffectClass option
    }

// Meta
and FragmentRefSpec =
    {
      Name: string
      Args: Map<string, FragmentArg> option
    }

// Meta
and SwitchSpec =
    {
      Cases: SwitchCase list
      Default: Node
      StateKey: string
    }

// Meta
and MountSpec =
    {
      Capabilities: string list
      Channel: GuestChannel
      Inputs: Map<string, FragmentArg> option
      OnBubble: unit option
      ScopeId: string
    }

and [<RequireQualifiedAccess>] NodeKind =
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
    | Box of BoxSpec
    | SplitPanel of SplitPanelSpec
    | SummaryList of SummaryListSpec
    | Disclosure of DisclosureSpec
    | Modal of ModalSpec
    | ScrollArea of ScrollAreaSpec
    | Tabs of TabsSpec
    | Stepper of StepperSpec
    | Button of ButtonSpec
    | Select of SelectSpec
    | FileUpload of FileUploadSpec
    | Form of FormSpec
    | Filters of FiltersSpec
    | DataGrid of DataGridSpec
    | Chart of ChartSpec
    | Map of MapSpec
    | Custom of CustomSpec
    | ErrorBoundary of ErrorBoundarySpec
    | FragmentDecl of FragmentDeclSpec
    | FragmentRef of FragmentRefSpec
    | Switch of SwitchSpec
    | Mount of MountSpec

and Node = { Id: string; Kind: NodeKind }

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

let rec private encNode (n: Node) : JVal =
    let kind =
        match n.Kind with
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

    JObj [ "id", JStr n.Id; "kind", kind ]

and private encTextSource (v: TextSource) : JVal =
    match v with
    | TextSource.Literal text -> JStr text
    | TextSource.Bound binding -> Canon.typed "Bound" [ "binding", (encBinding JStr) binding ]

and private encBinding<'T> (encT: 'T -> JVal) (v: Binding<'T>) : JVal =
    match v with
    | Binding.Static value -> Canon.typed "Static" ([ (value |> Option.map (fun v -> "value", encT v)) ] |> List.choose id)
    | Binding.Query (dependsOn, name) -> Canon.typed "Query" ([ (dependsOn |> Option.map (fun v -> "dependsOn", JArr(List.map JStr v))); Some("name", JStr name) ] |> List.choose id)
    | Binding.Filter name -> Canon.typed "Filter" [ "name", JStr name ]
    | Binding.State (defaultValue, key) -> Canon.typed "State" ([ (defaultValue |> Option.map (fun v -> "defaultValue", encT v)); Some("key", JStr key) ] |> List.choose id)
    | Binding.Computed fn -> Canon.typed "Computed" [ "fn", JStr "<closure>" ]
    | Binding.Local (flushOn, format, initialFrom, onCommit, parse) -> Canon.typed "Local" ([ Some("flushOn", encLocalFlushTrigger flushOn); Some("format", JStr "<closure>"); Some("initialFrom", (encBinding encT) initialFrom); (onCommit |> Option.map (fun v -> "onCommit", JStr "<closure>")); Some("parse", JStr "<closure>") ] |> List.choose id)
    | Binding.Format (format, locale, source) -> Canon.typed "Format" [ "format", encFormat format; "locale", encLocaleSource locale; "source", (encBinding JFloat) source ]
    | Binding.Invoke (args, capabilityId) -> Canon.typed "Invoke" [ "args", JArr(List.map encInvokeArg args); "capabilityId", JStr capabilityId ]

and private encCellFormat (v: CellFormat) : JVal =
    match v with
    | CellFormat.None -> Canon.typed "None" [  ]
    | CellFormat.Number decimals -> Canon.typed "Number" ([ (decimals |> Option.map (fun v -> "decimals", JInt v)) ] |> List.choose id)
    | CellFormat.Currency code -> Canon.typed "Currency" [ "code", JStr code ]
    | CellFormat.Percent decimals -> Canon.typed "Percent" ([ (decimals |> Option.map (fun v -> "decimals", JInt v)) ] |> List.choose id)
    | CellFormat.SignificantDigits digits -> Canon.typed "SignificantDigits" [ "digits", JInt digits ]
    | CellFormat.Date format -> Canon.typed "Date" [ "format", JStr format ]
    | CellFormat.Custom fn -> Canon.typed "Custom" [ "fn", JStr "<closure>" ]

and private encAction (v: Action) : JVal =
    match v with
    | Action.Chain ops -> Canon.typed "Chain" [ "ops", JArr(List.map encAction ops) ]
    | Action.WriteToClipboard text -> Canon.typed "WriteToClipboard" [ "text", JStr text ]
    | Action.Dispatch -> Canon.typed "Dispatch" [  ]
    | Action.Invoke (args, capabilityId) -> Canon.typed "Invoke" [ "args", JArr(List.map encInvokeArg args); "capabilityId", JStr capabilityId ]
    | Action.ReadFileBody (encoding, fileRef, onRead) -> Canon.typed "ReadFileBody" ([ Some("encoding", encFileReadEncoding encoding); Some("fileRef", JStr fileRef); (onRead |> Option.map (fun v -> "onRead", JStr "<closure>")) ] |> List.choose id)
    | Action.Call (endpoint, into, onResult) -> Canon.typed "Call" ([ Some("endpoint", JStr endpoint); (into |> Option.map (fun v -> "into", encCallResultTarget v)); (onResult |> Option.map (fun v -> "onResult", JStr "<closure>")) ] |> List.choose id)
    | Action.Navigate route -> Canon.typed "Navigate" [ "route", JStr route ]
    | Action.CommitLocal nodeId -> Canon.typed "CommitLocal" [ "nodeId", JStr nodeId ]
    | Action.Notify (channel, payload) -> Canon.typed "Notify" [ "channel", JStr channel; "payload", id payload ]
    | Action.SetState (key, value) -> Canon.typed "SetState" [ "key", JStr key; "value", id value ]
    | Action.AiTool (args, toolName) -> Canon.typed "AiTool" [ "args", id args; "toolName", JStr toolName ]

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
    | LayoutMode.Flex (direction, wrap) -> Canon.typed "Flex" [ "direction", encOrientation direction; "wrap", JBool wrap ]
    | LayoutMode.Grid (cols, templateColumns) -> Canon.typed "Grid" ([ Some("cols", JInt cols); (templateColumns |> Option.map (fun v -> "templateColumns", JStr v)) ] |> List.choose id)

and private encFormFieldKind (v: FormFieldKind) : JVal =
    match v with
    | FormFieldKind.Text (onChange, value) -> Canon.typed "Text" ([ (onChange |> Option.map (fun v -> "onChange", JStr "<closure>")); Some("value", (encBinding JStr) value) ] |> List.choose id)
    | FormFieldKind.Number (onChange, value) -> Canon.typed "Number" ([ (onChange |> Option.map (fun v -> "onChange", JStr "<closure>")); Some("value", (encBinding JFloat) value) ] |> List.choose id)
    | FormFieldKind.Checkbox (onToggle, value) -> Canon.typed "Checkbox" ([ (onToggle |> Option.map (fun v -> "onToggle", JStr "<closure>")); Some("value", (encBinding JBool) value) ] |> List.choose id)
    | FormFieldKind.Choice (onChange, options, value) -> Canon.typed "Choice" ([ (onChange |> Option.map (fun v -> "onChange", JStr "<closure>")); Some("options", (encBinding (fun __xs -> JArr(List.map encSelectOption __xs))) options); Some("value", (encBinding JStr) value) ] |> List.choose id)
    | FormFieldKind.TextArea (onChange, rows, value) -> Canon.typed "TextArea" ([ (onChange |> Option.map (fun v -> "onChange", JStr "<closure>")); Some("rows", JInt rows); Some("value", (encBinding JStr) value) ] |> List.choose id)
    | FormFieldKind.RangedNumber (onChange, value, min, max, step) -> Canon.typed "RangedNumber" ([ (onChange |> Option.map (fun v -> "onChange", JStr "<closure>")); Some("value", (encBinding JFloat) value); (min |> Option.map (fun v -> "min", JFloat v)); (max |> Option.map (fun v -> "max", JFloat v)); (step |> Option.map (fun v -> "step", JFloat v)) ] |> List.choose id)
    | FormFieldKind.SegmentedChoice (onChange, options, orientation, value) -> Canon.typed "SegmentedChoice" ([ (onChange |> Option.map (fun v -> "onChange", JStr "<closure>")); Some("options", (encBinding (fun __xs -> JArr(List.map encSelectOption __xs))) options); Some("orientation", encOrientation orientation); Some("value", (encBinding JStr) value) ] |> List.choose id)
    | FormFieldKind.Date (onChange, value, variant, min, max, step) -> Canon.typed "Date" ([ (onChange |> Option.map (fun v -> "onChange", JStr "<closure>")); Some("value", (encBinding JStr) value); Some("variant", encDateVariant variant); (min |> Option.map (fun v -> "min", JStr v)); (max |> Option.map (fun v -> "max", JStr v)); (step |> Option.map (fun v -> "step", JFloat v)) ] |> List.choose id)

and private encFilterKind (v: FilterKind) : JVal =
    match v with
    | FilterKind.Text (onChange, value) -> Canon.typed "Text" ([ (onChange |> Option.map (fun v -> "onChange", JStr "<closure>")); Some("value", (encBinding JStr) value) ] |> List.choose id)
    | FilterKind.Choice (onChange, options, value) -> Canon.typed "Choice" ([ (onChange |> Option.map (fun v -> "onChange", JStr "<closure>")); Some("options", (encBinding (fun __xs -> JArr(List.map encSelectOption __xs))) options); Some("value", (encBinding JStr) value) ] |> List.choose id)
    | FilterKind.Range (onChange, value) -> Canon.typed "Range" ([ (onChange |> Option.map (fun v -> "onChange", JStr "<closure>")); Some("value", JStr "<opaque>") ] |> List.choose id)
    | FilterKind.SegmentedChoice (onChange, options, orientation, value) -> Canon.typed "SegmentedChoice" ([ (onChange |> Option.map (fun v -> "onChange", JStr "<closure>")); Some("options", (encBinding (fun __xs -> JArr(List.map encSelectOption __xs))) options); Some("orientation", encOrientation orientation); Some("value", (encBinding JStr) value) ] |> List.choose id)

and private encColumnWidth (v: ColumnWidth) : JVal =
    match v with
    | ColumnWidth.Auto -> Canon.typed "Auto" [  ]
    | ColumnWidth.Fixed pixels -> Canon.typed "Fixed" [ "pixels", JInt pixels ]
    | ColumnWidth.Flex weight -> Canon.typed "Flex" [ "weight", JFloat weight ]

and private encCellKindErased (v: CellKindErased) : JVal =
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
    | CellKindErased.Progress (fractionFn, labelFn) -> Canon.typed "Progress" [ "fractionFn", JStr "<closure>"; "labelFn", JStr "<closure>" ]
    | CellKindErased.Custom fn -> Canon.typed "Custom" [ "fn", JStr "<closure>" ]

and private encHoleValueSpace (v: HoleValueSpace) : JVal =
    match v with
    | HoleValueSpace.IntRange (max, min) -> Canon.typed "IntRange" [ "max", JInt max; "min", JInt min ]
    | HoleValueSpace.FloatRange (max, min) -> Canon.typed "FloatRange" [ "max", JFloat max; "min", JFloat min ]
    | HoleValueSpace.StringLen (maxLen, minLen) -> Canon.typed "StringLen" [ "maxLen", JInt maxLen; "minLen", JInt minLen ]
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
    | HoleDecl.Value (``default``, name, space) -> Canon.typed "Value" ([ (``default`` |> Option.map (fun v -> "default", encScalar v)); Some("name", JStr name); Some("space", encHoleValueSpace space) ] |> List.choose id)
    | HoleDecl.Slot (kindConstraint, name) -> Canon.typed "Slot" ([ (kindConstraint |> Option.map (fun v -> "kindConstraint", JStr v)); Some("name", JStr name) ] |> List.choose id)
    | HoleDecl.Repeat (countSpace, name) -> Canon.typed "Repeat" [ "countSpace", encHoleValueSpace countSpace; "name", JStr name ]

and private encFragmentArg (v: FragmentArg) : JVal =
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
    | Shape.Rectangle (cornerRadius, height, style, width, x, y) -> Canon.typed "Rectangle" ([ (cornerRadius |> Option.map (fun v -> "cornerRadius", JFloat v)); Some("height", JFloat height); Some("style", encDrawStyle style); Some("width", JFloat width); Some("x", JFloat x); Some("y", JFloat y) ] |> List.choose id)
    | Shape.Line (style, x1, x2, y1, y2) -> Canon.typed "Line" [ "style", encDrawStyle style; "x1", JFloat x1; "x2", JFloat x2; "y1", JFloat y1; "y2", JFloat y2 ]
    | Shape.Polyline (points, style) -> Canon.typed "Polyline" [ "points", JArr(List.map encDrawPoint points); "style", encDrawStyle style ]
    | Shape.Polygon (points, style) -> Canon.typed "Polygon" [ "points", JArr(List.map encDrawPoint points); "style", encDrawStyle style ]
    | Shape.Curve (commands, style) -> Canon.typed "Curve" [ "commands", JArr(List.map encCurveCommand commands); "style", encDrawStyle style ]
    | Shape.Circle (cx, cy, r, style) -> Canon.typed "Circle" [ "cx", JFloat cx; "cy", JFloat cy; "r", JFloat r; "style", encDrawStyle style ]
    | Shape.Ellipse (cx, cy, rx, ry, style) -> Canon.typed "Ellipse" [ "cx", JFloat cx; "cy", JFloat cy; "rx", JFloat rx; "ry", JFloat ry; "style", encDrawStyle style ]
    | Shape.Label (style, text, x, y) -> Canon.typed "Label" [ "style", encDrawStyle style; "text", encTextSource text; "x", JFloat x; "y", JFloat y ]

and private encSwitchCase (s: SwitchCase) : JVal =
    JObj([ Some("child", encNode s.Child); Some("match", JStr s.Match) ] |> List.choose id)

and private encGuestChannel (s: GuestChannel) : JVal =
    JObj([ Some("direction", encChannelDirection s.Direction); (s.MessageShape |> Option.map (fun v -> "messageShape", JStr v)) ] |> List.choose id)

and private encDrawPoint (s: DrawPoint) : JVal =
    JObj([ Some("x", JFloat s.X); Some("y", JFloat s.Y) ] |> List.choose id)

and private encViewBox (s: ViewBox) : JVal =
    JObj([ Some("height", JFloat s.Height); Some("minX", JFloat s.MinX); Some("minY", JFloat s.MinY); Some("width", JFloat s.Width) ] |> List.choose id)

and private encDrawStyle (s: DrawStyle) : JVal =
    JObj([ (s.Emphasis |> Option.map (fun v -> "emphasis", encEmphasis v)); (s.Fill |> Option.map (fun v -> "fill", (encBinding JStr) v)); (s.FontFamily |> Option.map (fun v -> "fontFamily", JStr v)); (s.FontSize |> Option.map (fun v -> "fontSize", JFloat v)); (s.Opacity |> Option.map (fun v -> "opacity", (encBinding JFloat) v)); (s.Stroke |> Option.map (fun v -> "stroke", (encBinding JStr) v)); (s.StrokeWidth |> Option.map (fun v -> "strokeWidth", (encBinding JFloat) v)); (s.TextAnchor |> Option.map (fun v -> "textAnchor", encTextAnchor v)) ] |> List.choose id)

and private encInvokeArg (s: InvokeArg) : JVal =
    JObj([ Some("addr", JStr s.Addr); Some("value", JStr s.Value) ] |> List.choose id)

and private encSelectOption (s: SelectOption) : JVal =
    JObj([ Some("label", JStr s.Label); Some("value", JStr s.Value) ] |> List.choose id)

and private encMapMarker (s: MapMarker) : JVal =
    JObj([ Some("label", JStr s.Label); Some("latitude", JFloat s.Latitude); Some("longitude", JFloat s.Longitude) ] |> List.choose id)

and private encStaticRows (s: StaticRows) : JVal =
    JObj([ Some("headers", JArr(List.map JStr s.Headers)); Some("rows", JArr(List.map (fun __xs -> JArr(List.map JStr __xs)) s.Rows)) ] |> List.choose id)

and private encFormField (s: FormField) : JVal =
    JObj([ Some("id", JStr s.Id); Some("kind", encFormFieldKind s.Kind); Some("label", encTextSource s.Label); Some("required", JBool s.Required); (s.Help |> Option.map (fun v -> "help", encTextSource v)) ] |> List.choose id)

and private encFilterSpec (s: FilterSpec) : JVal =
    JObj([ Some("kind", encFilterKind s.Kind); Some("label", encTextSource s.Label); Some("name", JStr s.Name) ] |> List.choose id)

and private encTabHeader (s: TabHeader) : JVal =
    JObj([ Some("label", encTextSource s.Label); (s.Icon |> Option.map (fun v -> "icon", JStr v)); (s.Disabled |> Option.map (fun v -> "disabled", (encBinding JBool) v)) ] |> List.choose id)

and private encColumnErased (s: ColumnErased) : JVal =
    JObj([ (if s.Format = CellFormat.None then None else Some("format", encCellFormat s.Format)); Some("kind", encCellKindErased s.Kind); Some("label", JStr s.Label); Some("value", JStr "<closure>"); (if s.Width = ColumnWidth.Auto then None else Some("width", encColumnWidth s.Width)) ] |> List.choose id)

and private encButtonGroupItem (s: ButtonGroupItem) : JVal =
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
    Canon.typed "Metric" ([ Some("label", encTextSource s.Label); Some("value", (encBinding JFloat) s.Value); (if s.Format = CellFormat.None then None else Some("format", encCellFormat s.Format)); (if s.Tone = ToneVariant.Default then None else Some("tone", encToneVariant s.Tone)); (if s.Weight = StyleWeight.Standard then None else Some("weight", encStyleWeight s.Weight)); (if s.Emphasis = Emphasis.Normal then None else Some("emphasis", encEmphasis s.Emphasis)); (s.Trend |> Option.map (fun v -> "trend", (encBinding JFloat) v)); (s.TrendFormat |> Option.map (fun v -> "trendFormat", encCellFormat v)); (s.Icon |> Option.map (fun v -> "icon", JStr v)); (s.Subtext |> Option.map (fun v -> "subtext", encTextSource v)) ] |> List.choose id)

and private encLabelValueRowSpec (s: LabelValueRowSpec) : JVal =
    Canon.typed "LabelValueRow" ([ (if s.Emphasis = false then None else Some("emphasis", JBool s.Emphasis)); (if s.Format = CellFormat.None then None else Some("format", encCellFormat s.Format)); Some("label", encTextSource s.Label); Some("value", (encBinding JFloat) s.Value); (s.Help |> Option.map (fun v -> "help", encTextSource v)) ] |> List.choose id)

and private encFactSpec (s: FactSpec) : JVal =
    Canon.typed "Fact" ([ (if s.Emphasis = false then None else Some("emphasis", JBool s.Emphasis)); (s.Help |> Option.map (fun v -> "help", encTextSource v)); (s.Icon |> Option.map (fun v -> "icon", JStr v)); Some("label", encTextSource s.Label); (if s.Tone = ToneVariant.Default then None else Some("tone", encToneVariant s.Tone)); Some("value", encTextSource s.Value) ] |> List.choose id)

and private encSparklineSpec (s: SparklineSpec) : JVal =
    Canon.typed "Sparkline" ([ Some("source", (encBinding (fun __xs -> JArr(List.map JInt __xs))) s.Source) ] |> List.choose id)

and private encCodeBlockSpec (s: CodeBlockSpec) : JVal =
    Canon.typed "CodeBlock" ([ Some("code", JStr s.Code); Some("copyable", JBool s.Copyable); Some("highlightLines", JArr(List.map JInt s.HighlightLines)); Some("language", JStr s.Language); Some("lineNumbers", JBool s.LineNumbers) ] |> List.choose id)

and private encToastSpec (s: ToastSpec) : JVal =
    Canon.typed "Toast" ([ (if s.Dismissable = true then None else Some("dismissable", JBool s.Dismissable)); Some("message", encTextSource s.Message); Some("open", (encBinding JBool) s.Open); (if s.Tone = ToneVariant.Default then None else Some("tone", encToneVariant s.Tone)) ] |> List.choose id)

and private encDrawingSpec (s: DrawingSpec) : JVal =
    Canon.typed "Drawing" ([ (s.Description |> Option.map (fun v -> "description", encTextSource v)); Some("shapes", JArr(List.map encShape s.Shapes)); Some("style", encDrawStyle s.Style); (s.Title |> Option.map (fun v -> "title", encTextSource v)); Some("viewBox", encViewBox s.ViewBox) ] |> List.choose id)

and private encBoxSpec (s: BoxSpec) : JVal =
    Canon.typed "Box" ([ Some("children", JArr(List.map encNode s.Children)); (s.Heading |> Option.map (fun v -> "heading", encTextSource v)); Some("layout", encLayoutMode s.Layout); Some("role", encBoxRole s.Role) ] |> List.choose id)

and private encSplitPanelSpec (s: SplitPanelSpec) : JVal =
    Canon.typed "SplitPanel" ([ Some("children", JArr(List.map encNode s.Children)); Some("weight", JFloat s.Weight) ] |> List.choose id)

and private encSummaryListSpec (s: SummaryListSpec) : JVal =
    Canon.typed "SummaryList" ([ Some("children", JArr(List.map encNode s.Children)); (s.Heading |> Option.map (fun v -> "heading", encTextSource v)) ] |> List.choose id)

and private encDisclosureSpec (s: DisclosureSpec) : JVal =
    Canon.typed "Disclosure" ([ Some("children", JArr(List.map encNode s.Children)); Some("defaultOpen", JBool s.DefaultOpen); Some("heading", encTextSource s.Heading); (s.OnToggle |> Option.map (fun v -> "onToggle", JStr "<closure>")); Some("open", (encBinding JBool) s.Open) ] |> List.choose id)

and private encModalSpec (s: ModalSpec) : JVal =
    Canon.typed "Modal" ([ Some("children", JArr(List.map encNode s.Children)); Some("dismissable", JBool s.Dismissable); Some("onDismiss", encAction s.OnDismiss); Some("open", (encBinding JBool) s.Open); (s.Heading |> Option.map (fun v -> "heading", encTextSource v)) ] |> List.choose id)

and private encScrollAreaSpec (s: ScrollAreaSpec) : JVal =
    Canon.typed "ScrollArea" ([ Some("children", JArr(List.map encNode s.Children)); Some("orientation", encScrollOrientation s.Orientation); (s.MaxHeight |> Option.map (fun v -> "maxHeight", JInt v)); (s.MaxWidth |> Option.map (fun v -> "maxWidth", JInt v)) ] |> List.choose id)

and private encTabsSpec (s: TabsSpec) : JVal =
    Canon.typed "Tabs" ([ Some("activeIndex", (encBinding JInt) s.ActiveIndex); Some("children", JArr(List.map encNode s.Children)); (s.OnSelect |> Option.map (fun v -> "onSelect", JStr "<closure>")); (s.OnSelectTag |> Option.map (fun v -> "onSelectTag", JStr "<closure>")); (s.TabHeaders |> Option.map (fun v -> "tabHeaders", JArr(List.map encTabHeader v))); (s.TabTags |> Option.map (fun v -> "tabTags", JArr(List.map JStr v))); (s.ActiveTag |> Option.map (fun v -> "activeTag", (encBinding JStr) v)) ] |> List.choose id)

and private encStepperSpec (s: StepperSpec) : JVal =
    Canon.typed "Stepper" ([ Some("activeStep", (encBinding JInt) s.ActiveStep); Some("children", JArr(List.map encNode s.Children)); (s.OnSelect |> Option.map (fun v -> "onSelect", JStr "<closure>")) ] |> List.choose id)

and private encButtonSpec (s: ButtonSpec) : JVal =
    Canon.typed "Button" ([ Some("label", encTextSource s.Label); Some("onClick", encAction s.OnClick); Some("variant", encButtonVariant s.Variant); (s.Icon |> Option.map (fun v -> "icon", JStr v)); (s.Tooltip |> Option.map (fun v -> "tooltip", encTextSource v)); (s.Disabled |> Option.map (fun v -> "disabled", (encBinding JBool) v)) ] |> List.choose id)

and private encSelectSpec (s: SelectSpec) : JVal =
    Canon.typed "Select" ([ Some("label", encTextSource s.Label); (s.OnChange |> Option.map (fun v -> "onChange", JStr "<closure>")); (s.OnChangeMulti |> Option.map (fun v -> "onChangeMulti", JStr "<closure>")); Some("source", (encBinding (fun __xs -> JArr(List.map encSelectOption __xs))) s.Source); Some("value", (encBinding JStr) s.Value); (s.Placeholder |> Option.map (fun v -> "placeholder", encTextSource v)); (s.Disabled |> Option.map (fun v -> "disabled", (encBinding JBool) v)); (s.Multiple |> Option.map (fun v -> "multiple", JBool v)); (s.Values |> Option.map (fun v -> "values", (encBinding (fun __xs -> JArr(List.map JStr __xs))) v)) ] |> List.choose id)

and private encFileUploadSpec (s: FileUploadSpec) : JVal =
    Canon.typed "FileUpload" ([ Some("accept", JArr(List.map JStr s.Accept)); Some("label", encTextSource s.Label); Some("multiple", JBool s.Multiple); (s.OnSelect |> Option.map (fun v -> "onSelect", JStr "<closure>")); (s.Disabled |> Option.map (fun v -> "disabled", (encBinding JBool) v)) ] |> List.choose id)

and private encFormSpec (s: FormSpec) : JVal =
    Canon.typed "Form" ([ Some("fields", JArr(List.map encFormField s.Fields)); Some("onSubmit", encAction s.OnSubmit); Some("submitLabel", encTextSource s.SubmitLabel); (s.Disabled |> Option.map (fun v -> "disabled", (encBinding JBool) v)) ] |> List.choose id)

and private encFiltersSpec (s: FiltersSpec) : JVal =
    Canon.typed "Filters" ([ Some("items", JArr(List.map encFilterSpec s.Items)) ] |> List.choose id)

and private encDataGridSpec (s: DataGridSpec) : JVal =
    Canon.typed "DataGrid" ([ Some("columns", JArr(List.map encColumnErased s.Columns)); (if s.Editable = false then None else Some("editable", JBool s.Editable)); (s.RowKey |> Option.map (fun v -> "rowKey", JStr "<closure>")); Some("source", (encBinding (fun _ -> JStr "<opaque>")) s.Source); (s.StaticRows |> Option.map (fun v -> "staticRows", encStaticRows v)); (s.OnRowClick |> Option.map (fun v -> "onRowClick", JStr "<closure>")) ] |> List.choose id)

and private encChartSpec (s: ChartSpec) : JVal =
    Canon.typed "Chart" ([ Some("kind", encChartKind s.Kind); Some("source", (encBinding (fun _ -> JStr "<opaque>")) s.Source); Some("stacked", JBool s.Stacked); Some("xField", JStr s.XField); Some("yFields", JArr(List.map JStr s.YFields)); (s.Title |> Option.map (fun v -> "title", encTextSource v)); (s.OnPointClick |> Option.map (fun v -> "onPointClick", JStr "<closure>")) ] |> List.choose id)

and private encMapSpec (s: MapSpec) : JVal =
    Canon.typed "Map" ([ Some("centreLatitude", JFloat s.CentreLatitude); Some("centreLongitude", JFloat s.CentreLongitude); Some("source", (encBinding (fun __xs -> JArr(List.map encMapMarker __xs))) s.Source); Some("zoom", JInt s.Zoom); (s.OnMarkerClick |> Option.map (fun v -> "onMarkerClick", JStr "<closure>")) ] |> List.choose id)

and private encCustomSpec (s: CustomSpec) : JVal =
    Canon.typed "Custom" ([ Some("moduleId", JStr s.ModuleId); Some("componentId", JStr s.ComponentId); Some("props", (fun __m -> JObj(Map.toList __m |> List.map (fun (k, v) -> k, (fun _ -> JStr "<opaque>") v))) s.Props); (s.ContentHash |> Option.map (fun v -> "contentHash", encContentHash v)); (s.ExposedNodeIds |> Option.map (fun v -> "exposedNodeIds", JArr(List.map JStr v))) ] |> List.choose id)

and private encErrorBoundarySpec (s: ErrorBoundarySpec) : JVal =
    Canon.typed "ErrorBoundary" ([ Some("child", encNode s.Child); Some("fallback", encNode s.Fallback) ] |> List.choose id)

and private encFragmentDeclSpec (s: FragmentDeclSpec) : JVal =
    Canon.typed "FragmentDecl" ([ Some("body", encNode s.Body); Some("name", JStr s.Name); (s.Holes |> Option.map (fun v -> "holes", JArr(List.map encHoleDecl v))); (s.Effect |> Option.map (fun v -> "effect", encEffectClass v)) ] |> List.choose id)

and private encFragmentRefSpec (s: FragmentRefSpec) : JVal =
    Canon.typed "FragmentRef" ([ Some("name", JStr s.Name); (s.Args |> Option.map (fun v -> "args", (fun __m -> JObj(Map.toList __m |> List.map (fun (k, v) -> k, encFragmentArg v))) v)) ] |> List.choose id)

and private encSwitchSpec (s: SwitchSpec) : JVal =
    Canon.typed "Switch" ([ Some("cases", JArr(List.map encSwitchCase s.Cases)); Some("default", encNode s.Default); Some("stateKey", JStr s.StateKey) ] |> List.choose id)

and private encMountSpec (s: MountSpec) : JVal =
    Canon.typed "Mount" ([ Some("capabilities", JArr(List.map JStr s.Capabilities)); Some("channel", encGuestChannel s.Channel); (s.Inputs |> Option.map (fun v -> "inputs", (fun __m -> JObj(Map.toList __m |> List.map (fun (k, v) -> k, encFragmentArg v))) v)); (s.OnBubble |> Option.map (fun v -> "onBubble", JStr "<closure>")); Some("scopeId", JStr s.ScopeId) ] |> List.choose id)

let encodeNode (n: Node) : string = Canon.render (encNode n)

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

let rec private decNodeKind (j: JVal) : Result<NodeKind, string> =
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

and private decNode (j: JVal) : Result<Node, string> =
    dObj j |> Result.bind (fun __fs ->
    dReq "id" __fs dStr |> Result.bind (fun id ->
    dReq "kind" __fs decNodeKind |> Result.bind (fun kind ->
    Ok { Id = id; Kind = kind })))

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
            dOpt "dependsOn" __fs (dList dStr) |> Result.bind (fun dependsOn ->
            dReq "name" __fs dStr |> Result.bind (fun name ->
            Ok(Binding.Query(dependsOn, name))))
        | "Filter" ->
            dReq "name" __fs dStr |> Result.bind (fun name ->
            Ok(Binding.Filter(name)))
        | "State" ->
            dOpt "defaultValue" __fs decT |> Result.bind (fun defaultValue ->
            dReq "key" __fs dStr |> Result.bind (fun key ->
            Ok(Binding.State(defaultValue, key))))
        | "Computed" ->
            Ok() |> Result.bind (fun fn ->
            Ok(Binding.Computed(fn)))
        | "Local" ->
            dReq "flushOn" __fs decLocalFlushTrigger |> Result.bind (fun flushOn ->
            Ok() |> Result.bind (fun format ->
            dReq "initialFrom" __fs (decBinding decT) |> Result.bind (fun initialFrom ->
            dPresent "onCommit" __fs |> Result.bind (fun onCommit ->
            Ok() |> Result.bind (fun parse ->
            Ok(Binding.Local(flushOn, format, initialFrom, onCommit, parse)))))))
        | "Format" ->
            dReq "format" __fs decFormat |> Result.bind (fun format ->
            dReq "locale" __fs decLocaleSource |> Result.bind (fun locale ->
            dReq "source" __fs (decBinding dFloat) |> Result.bind (fun source ->
            Ok(Binding.Format(format, locale, source)))))
        | "Invoke" ->
            dReq "args" __fs (dList decInvokeArg) |> Result.bind (fun args ->
            dReq "capabilityId" __fs dStr |> Result.bind (fun capabilityId ->
            Ok(Binding.Invoke(args, capabilityId))))
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
            Ok() |> Result.bind (fun fn ->
            Ok(CellFormat.Custom(fn)))
        | __other -> Error ("unknown CellFormat case: " + __other))
    | _ -> Error "expected a CellFormat object"

and private decAction (j: JVal) : Result<Action, string> =
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
        | "Dispatch" -> Ok Action.Dispatch
        | "Invoke" ->
            dReq "args" __fs (dList decInvokeArg) |> Result.bind (fun args ->
            dReq "capabilityId" __fs dStr |> Result.bind (fun capabilityId ->
            Ok(Action.Invoke(args, capabilityId))))
        | "ReadFileBody" ->
            dReq "encoding" __fs decFileReadEncoding |> Result.bind (fun encoding ->
            dReq "fileRef" __fs dStr |> Result.bind (fun fileRef ->
            dPresent "onRead" __fs |> Result.bind (fun onRead ->
            Ok(Action.ReadFileBody(encoding, fileRef, onRead)))))
        | "Call" ->
            dReq "endpoint" __fs dStr |> Result.bind (fun endpoint ->
            dOpt "into" __fs decCallResultTarget |> Result.bind (fun into ->
            dPresent "onResult" __fs |> Result.bind (fun onResult ->
            Ok(Action.Call(endpoint, into, onResult)))))
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
            dReq "args" __fs dJson |> Result.bind (fun args ->
            dReq "toolName" __fs dStr |> Result.bind (fun toolName ->
            Ok(Action.AiTool(args, toolName))))
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
            Ok(LayoutMode.Flex(direction, wrap))))
        | "Grid" ->
            dReq "cols" __fs dInt |> Result.bind (fun cols ->
            dOpt "templateColumns" __fs dStr |> Result.bind (fun templateColumns ->
            Ok(LayoutMode.Grid(cols, templateColumns))))
        | __other -> Error ("unknown LayoutMode case: " + __other))
    | _ -> Error "expected a LayoutMode object"

and private decFormFieldKind (j: JVal) : Result<FormFieldKind, string> =
    match j with
    | JObj __fs when (__fs |> List.exists (fun (k, _) -> k = "$type")) ->
        dTag __fs |> Result.bind (fun __t ->
        match __t with
        | "Text" ->
            dPresent "onChange" __fs |> Result.bind (fun onChange ->
            dReq "value" __fs (decBinding dStr) |> Result.bind (fun value ->
            Ok(FormFieldKind.Text(onChange, value))))
        | "Number" ->
            dPresent "onChange" __fs |> Result.bind (fun onChange ->
            dReq "value" __fs (decBinding dFloat) |> Result.bind (fun value ->
            Ok(FormFieldKind.Number(onChange, value))))
        | "Checkbox" ->
            dPresent "onToggle" __fs |> Result.bind (fun onToggle ->
            dReq "value" __fs (decBinding dBool) |> Result.bind (fun value ->
            Ok(FormFieldKind.Checkbox(onToggle, value))))
        | "Choice" ->
            dPresent "onChange" __fs |> Result.bind (fun onChange ->
            dReq "options" __fs (decBinding (dList decSelectOption)) |> Result.bind (fun options ->
            dReq "value" __fs (decBinding dStr) |> Result.bind (fun value ->
            Ok(FormFieldKind.Choice(onChange, options, value)))))
        | "TextArea" ->
            dPresent "onChange" __fs |> Result.bind (fun onChange ->
            dReq "rows" __fs dInt |> Result.bind (fun rows ->
            dReq "value" __fs (decBinding dStr) |> Result.bind (fun value ->
            Ok(FormFieldKind.TextArea(onChange, rows, value)))))
        | "RangedNumber" ->
            dPresent "onChange" __fs |> Result.bind (fun onChange ->
            dReq "value" __fs (decBinding dFloat) |> Result.bind (fun value ->
            dOpt "min" __fs dFloat |> Result.bind (fun min ->
            dOpt "max" __fs dFloat |> Result.bind (fun max ->
            dOpt "step" __fs dFloat |> Result.bind (fun step ->
            Ok(FormFieldKind.RangedNumber(onChange, value, min, max, step)))))))
        | "SegmentedChoice" ->
            dPresent "onChange" __fs |> Result.bind (fun onChange ->
            dReq "options" __fs (decBinding (dList decSelectOption)) |> Result.bind (fun options ->
            dReq "orientation" __fs decOrientation |> Result.bind (fun orientation ->
            dReq "value" __fs (decBinding dStr) |> Result.bind (fun value ->
            Ok(FormFieldKind.SegmentedChoice(onChange, options, orientation, value))))))
        | "Date" ->
            dPresent "onChange" __fs |> Result.bind (fun onChange ->
            dReq "value" __fs (decBinding dStr) |> Result.bind (fun value ->
            dReq "variant" __fs decDateVariant |> Result.bind (fun variant ->
            dOpt "min" __fs dStr |> Result.bind (fun min ->
            dOpt "max" __fs dStr |> Result.bind (fun max ->
            dOpt "step" __fs dFloat |> Result.bind (fun step ->
            Ok(FormFieldKind.Date(onChange, value, variant, min, max, step))))))))
        | __other -> Error ("unknown FormFieldKind case: " + __other))
    | _ -> Error "expected a FormFieldKind object"

and private decFilterKind (j: JVal) : Result<FilterKind, string> =
    match j with
    | JObj __fs when (__fs |> List.exists (fun (k, _) -> k = "$type")) ->
        dTag __fs |> Result.bind (fun __t ->
        match __t with
        | "Text" ->
            dPresent "onChange" __fs |> Result.bind (fun onChange ->
            dReq "value" __fs (decBinding dStr) |> Result.bind (fun value ->
            Ok(FilterKind.Text(onChange, value))))
        | "Choice" ->
            dPresent "onChange" __fs |> Result.bind (fun onChange ->
            dReq "options" __fs (decBinding (dList decSelectOption)) |> Result.bind (fun options ->
            dReq "value" __fs (decBinding dStr) |> Result.bind (fun value ->
            Ok(FilterKind.Choice(onChange, options, value)))))
        | "Range" ->
            dPresent "onChange" __fs |> Result.bind (fun onChange ->
            Ok() |> Result.bind (fun value ->
            Ok(FilterKind.Range(onChange, value))))
        | "SegmentedChoice" ->
            dPresent "onChange" __fs |> Result.bind (fun onChange ->
            dReq "options" __fs (decBinding (dList decSelectOption)) |> Result.bind (fun options ->
            dReq "orientation" __fs decOrientation |> Result.bind (fun orientation ->
            dReq "value" __fs (decBinding dStr) |> Result.bind (fun value ->
            Ok(FilterKind.SegmentedChoice(onChange, options, orientation, value))))))
        | __other -> Error ("unknown FilterKind case: " + __other))
    | _ -> Error "expected a FilterKind object"

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

and private decCellKindErased (j: JVal) : Result<CellKindErased, string> =
    match j with
    | JObj __fs when (__fs |> List.exists (fun (k, _) -> k = "$type")) ->
        dTag __fs |> Result.bind (fun __t ->
        match __t with
        | "Text" -> Ok CellKindErased.Text
        | "Numeric" -> Ok CellKindErased.Numeric
        | "Date" -> Ok CellKindErased.Date
        | "Editable" ->
            dPresent "onEdit" __fs |> Result.bind (fun onEdit ->
            Ok(CellKindErased.Editable(onEdit)))
        | "Checkbox" ->
            Ok() |> Result.bind (fun get ->
            dPresent "onToggle" __fs |> Result.bind (fun onToggle ->
            Ok(CellKindErased.Checkbox(get, onToggle))))
        | "Button" ->
            dReq "label" __fs decTextSource |> Result.bind (fun label ->
            dPresent "onClick" __fs |> Result.bind (fun onClick ->
            Ok(CellKindErased.Button(label, onClick))))
        | "ButtonGroup" ->
            dReq "buttons" __fs (dList decButtonGroupItem) |> Result.bind (fun buttons ->
            Ok(CellKindErased.ButtonGroup(buttons)))
        | "Link" ->
            Ok() |> Result.bind (fun hrefFn ->
            Ok() |> Result.bind (fun labelFn ->
            Ok(CellKindErased.Link(hrefFn, labelFn))))
        | "Pill" ->
            Ok() |> Result.bind (fun labelFn ->
            Ok() |> Result.bind (fun toneFn ->
            Ok(CellKindErased.Pill(labelFn, toneFn))))
        | "Progress" ->
            Ok() |> Result.bind (fun fractionFn ->
            Ok() |> Result.bind (fun labelFn ->
            Ok(CellKindErased.Progress(fractionFn, labelFn))))
        | "Custom" ->
            Ok() |> Result.bind (fun fn ->
            Ok(CellKindErased.Custom(fn)))
        | __other -> Error ("unknown CellKindErased case: " + __other))
    | _ -> Error "expected a CellKindErased object"

and private decHoleValueSpace (j: JVal) : Result<HoleValueSpace, string> =
    match j with
    | JObj __fs when (__fs |> List.exists (fun (k, _) -> k = "$type")) ->
        dTag __fs |> Result.bind (fun __t ->
        match __t with
        | "IntRange" ->
            dReq "max" __fs dInt |> Result.bind (fun max ->
            dReq "min" __fs dInt |> Result.bind (fun min ->
            Ok(HoleValueSpace.IntRange(max, min))))
        | "FloatRange" ->
            dReq "max" __fs dFloat |> Result.bind (fun max ->
            dReq "min" __fs dFloat |> Result.bind (fun min ->
            Ok(HoleValueSpace.FloatRange(max, min))))
        | "StringLen" ->
            dReq "maxLen" __fs dInt |> Result.bind (fun maxLen ->
            dReq "minLen" __fs dInt |> Result.bind (fun minLen ->
            Ok(HoleValueSpace.StringLen(maxLen, minLen))))
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
            dOpt "default" __fs decScalar |> Result.bind (fun ``default`` ->
            dReq "name" __fs dStr |> Result.bind (fun name ->
            dReq "space" __fs decHoleValueSpace |> Result.bind (fun space ->
            Ok(HoleDecl.Value(``default``, name, space)))))
        | "Slot" ->
            dOpt "kindConstraint" __fs dStr |> Result.bind (fun kindConstraint ->
            dReq "name" __fs dStr |> Result.bind (fun name ->
            Ok(HoleDecl.Slot(kindConstraint, name))))
        | "Repeat" ->
            dReq "countSpace" __fs decHoleValueSpace |> Result.bind (fun countSpace ->
            dReq "name" __fs dStr |> Result.bind (fun name ->
            Ok(HoleDecl.Repeat(countSpace, name))))
        | __other -> Error ("unknown HoleDecl case: " + __other))
    | _ -> Error "expected a HoleDecl object"

and private decFragmentArg (j: JVal) : Result<FragmentArg, string> =
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
            dOpt "cornerRadius" __fs dFloat |> Result.bind (fun cornerRadius ->
            dReq "height" __fs dFloat |> Result.bind (fun height ->
            dReq "style" __fs decDrawStyle |> Result.bind (fun style ->
            dReq "width" __fs dFloat |> Result.bind (fun width ->
            dReq "x" __fs dFloat |> Result.bind (fun x ->
            dReq "y" __fs dFloat |> Result.bind (fun y ->
            Ok(Shape.Rectangle(cornerRadius, height, style, width, x, y))))))))
        | "Line" ->
            dReq "style" __fs decDrawStyle |> Result.bind (fun style ->
            dReq "x1" __fs dFloat |> Result.bind (fun x1 ->
            dReq "x2" __fs dFloat |> Result.bind (fun x2 ->
            dReq "y1" __fs dFloat |> Result.bind (fun y1 ->
            dReq "y2" __fs dFloat |> Result.bind (fun y2 ->
            Ok(Shape.Line(style, x1, x2, y1, y2)))))))
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
            dReq "style" __fs decDrawStyle |> Result.bind (fun style ->
            dReq "text" __fs decTextSource |> Result.bind (fun text ->
            dReq "x" __fs dFloat |> Result.bind (fun x ->
            dReq "y" __fs dFloat |> Result.bind (fun y ->
            Ok(Shape.Label(style, text, x, y))))))
        | __other -> Error ("unknown Shape case: " + __other))
    | _ -> Error "expected a Shape object"

and private decSwitchCase (j: JVal) : Result<SwitchCase, string> =
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
    dOpt "opacity" __fs (decBinding dFloat) |> Result.bind (fun opacity ->
    dOpt "stroke" __fs (decBinding dStr) |> Result.bind (fun stroke ->
    dOpt "strokeWidth" __fs (decBinding dFloat) |> Result.bind (fun strokeWidth ->
    dOpt "textAnchor" __fs decTextAnchor |> Result.bind (fun textAnchor ->
    Ok { Emphasis = emphasis; Fill = fill; FontFamily = fontFamily; FontSize = fontSize; Opacity = opacity; Stroke = stroke; StrokeWidth = strokeWidth; TextAnchor = textAnchor })))))))))

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
    dReq "headers" __fs (dList dStr) |> Result.bind (fun headers ->
    dReq "rows" __fs (dList (dList dStr)) |> Result.bind (fun rows ->
    Ok { Headers = headers; Rows = rows })))

and private decFormField (j: JVal) : Result<FormField, string> =
    dObj j |> Result.bind (fun __fs ->
    dReq "id" __fs dStr |> Result.bind (fun id ->
    dReq "kind" __fs decFormFieldKind |> Result.bind (fun kind ->
    dReq "label" __fs decTextSource |> Result.bind (fun label ->
    dReq "required" __fs dBool |> Result.bind (fun required ->
    dOpt "help" __fs decTextSource |> Result.bind (fun help ->
    Ok { Id = id; Kind = kind; Label = label; Required = required; Help = help }))))))

and private decFilterSpec (j: JVal) : Result<FilterSpec, string> =
    dObj j |> Result.bind (fun __fs ->
    dReq "kind" __fs decFilterKind |> Result.bind (fun kind ->
    dReq "label" __fs decTextSource |> Result.bind (fun label ->
    dReq "name" __fs dStr |> Result.bind (fun name ->
    Ok { Kind = kind; Label = label; Name = name }))))

and private decTabHeader (j: JVal) : Result<TabHeader, string> =
    dObj j |> Result.bind (fun __fs ->
    dReq "label" __fs decTextSource |> Result.bind (fun label ->
    dOpt "icon" __fs dStr |> Result.bind (fun icon ->
    dOpt "disabled" __fs (decBinding dBool) |> Result.bind (fun disabled ->
    Ok { Label = label; Icon = icon; Disabled = disabled }))))

and private decColumnErased (j: JVal) : Result<ColumnErased, string> =
    dObj j |> Result.bind (fun __fs ->
    dDef "format" __fs decCellFormat (CellFormat.None) |> Result.bind (fun format ->
    dReq "kind" __fs decCellKindErased |> Result.bind (fun kind ->
    dReq "label" __fs dStr |> Result.bind (fun label ->
    Ok() |> Result.bind (fun value ->
    dDef "width" __fs decColumnWidth (ColumnWidth.Auto) |> Result.bind (fun width ->
    Ok { Format = format; Kind = kind; Label = label; Value = value; Width = width }))))))

and private decButtonGroupItem (j: JVal) : Result<ButtonGroupItem, string> =
    dObj j |> Result.bind (fun __fs ->
    dReq "label" __fs decTextSource |> Result.bind (fun label ->
    dPresent "onClick" __fs |> Result.bind (fun onClick ->
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
    dReq "source" __fs (decBinding (dList dInt)) |> Result.bind (fun source ->
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

and private decBoxSpec (j: JVal) : Result<BoxSpec, string> =
    dObj j |> Result.bind (fun __fs ->
    dReq "children" __fs (dList decNode) |> Result.bind (fun children ->
    dOpt "heading" __fs decTextSource |> Result.bind (fun heading ->
    dReq "layout" __fs decLayoutMode |> Result.bind (fun layout ->
    dReq "role" __fs decBoxRole |> Result.bind (fun role ->
    Ok { Children = children; Heading = heading; Layout = layout; Role = role })))))

and private decSplitPanelSpec (j: JVal) : Result<SplitPanelSpec, string> =
    dObj j |> Result.bind (fun __fs ->
    dReq "children" __fs (dList decNode) |> Result.bind (fun children ->
    dReq "weight" __fs dFloat |> Result.bind (fun weight ->
    Ok { Children = children; Weight = weight })))

and private decSummaryListSpec (j: JVal) : Result<SummaryListSpec, string> =
    dObj j |> Result.bind (fun __fs ->
    dReq "children" __fs (dList decNode) |> Result.bind (fun children ->
    dOpt "heading" __fs decTextSource |> Result.bind (fun heading ->
    Ok { Children = children; Heading = heading })))

and private decDisclosureSpec (j: JVal) : Result<DisclosureSpec, string> =
    dObj j |> Result.bind (fun __fs ->
    dReq "children" __fs (dList decNode) |> Result.bind (fun children ->
    dReq "defaultOpen" __fs dBool |> Result.bind (fun defaultOpen ->
    dReq "heading" __fs decTextSource |> Result.bind (fun heading ->
    dPresent "onToggle" __fs |> Result.bind (fun onToggle ->
    dReq "open" __fs (decBinding dBool) |> Result.bind (fun ``open`` ->
    Ok { Children = children; DefaultOpen = defaultOpen; Heading = heading; OnToggle = onToggle; Open = ``open`` }))))))

and private decModalSpec (j: JVal) : Result<ModalSpec, string> =
    dObj j |> Result.bind (fun __fs ->
    dReq "children" __fs (dList decNode) |> Result.bind (fun children ->
    dReq "dismissable" __fs dBool |> Result.bind (fun dismissable ->
    dReq "onDismiss" __fs decAction |> Result.bind (fun onDismiss ->
    dReq "open" __fs (decBinding dBool) |> Result.bind (fun ``open`` ->
    dOpt "heading" __fs decTextSource |> Result.bind (fun heading ->
    Ok { Children = children; Dismissable = dismissable; OnDismiss = onDismiss; Open = ``open``; Heading = heading }))))))

and private decScrollAreaSpec (j: JVal) : Result<ScrollAreaSpec, string> =
    dObj j |> Result.bind (fun __fs ->
    dReq "children" __fs (dList decNode) |> Result.bind (fun children ->
    dReq "orientation" __fs decScrollOrientation |> Result.bind (fun orientation ->
    dOpt "maxHeight" __fs dInt |> Result.bind (fun maxHeight ->
    dOpt "maxWidth" __fs dInt |> Result.bind (fun maxWidth ->
    Ok { Children = children; Orientation = orientation; MaxHeight = maxHeight; MaxWidth = maxWidth })))))

and private decTabsSpec (j: JVal) : Result<TabsSpec, string> =
    dObj j |> Result.bind (fun __fs ->
    dReq "activeIndex" __fs (decBinding dInt) |> Result.bind (fun activeIndex ->
    dReq "children" __fs (dList decNode) |> Result.bind (fun children ->
    dPresent "onSelect" __fs |> Result.bind (fun onSelect ->
    dPresent "onSelectTag" __fs |> Result.bind (fun onSelectTag ->
    dOpt "tabHeaders" __fs (dList decTabHeader) |> Result.bind (fun tabHeaders ->
    dOpt "tabTags" __fs (dList dStr) |> Result.bind (fun tabTags ->
    dOpt "activeTag" __fs (decBinding dStr) |> Result.bind (fun activeTag ->
    Ok { ActiveIndex = activeIndex; Children = children; OnSelect = onSelect; OnSelectTag = onSelectTag; TabHeaders = tabHeaders; TabTags = tabTags; ActiveTag = activeTag }))))))))

and private decStepperSpec (j: JVal) : Result<StepperSpec, string> =
    dObj j |> Result.bind (fun __fs ->
    dReq "activeStep" __fs (decBinding dInt) |> Result.bind (fun activeStep ->
    dReq "children" __fs (dList decNode) |> Result.bind (fun children ->
    dPresent "onSelect" __fs |> Result.bind (fun onSelect ->
    Ok { ActiveStep = activeStep; Children = children; OnSelect = onSelect }))))

and private decButtonSpec (j: JVal) : Result<ButtonSpec, string> =
    dObj j |> Result.bind (fun __fs ->
    dReq "label" __fs decTextSource |> Result.bind (fun label ->
    dReq "onClick" __fs decAction |> Result.bind (fun onClick ->
    dReq "variant" __fs decButtonVariant |> Result.bind (fun variant ->
    dOpt "icon" __fs dStr |> Result.bind (fun icon ->
    dOpt "tooltip" __fs decTextSource |> Result.bind (fun tooltip ->
    dOpt "disabled" __fs (decBinding dBool) |> Result.bind (fun disabled ->
    Ok { Label = label; OnClick = onClick; Variant = variant; Icon = icon; Tooltip = tooltip; Disabled = disabled })))))))

and private decSelectSpec (j: JVal) : Result<SelectSpec, string> =
    dObj j |> Result.bind (fun __fs ->
    dReq "label" __fs decTextSource |> Result.bind (fun label ->
    dPresent "onChange" __fs |> Result.bind (fun onChange ->
    dPresent "onChangeMulti" __fs |> Result.bind (fun onChangeMulti ->
    dReq "source" __fs (decBinding (dList decSelectOption)) |> Result.bind (fun source ->
    dReq "value" __fs (decBinding dStr) |> Result.bind (fun value ->
    dOpt "placeholder" __fs decTextSource |> Result.bind (fun placeholder ->
    dOpt "disabled" __fs (decBinding dBool) |> Result.bind (fun disabled ->
    dOpt "multiple" __fs dBool |> Result.bind (fun multiple ->
    dOpt "values" __fs (decBinding (dList dStr)) |> Result.bind (fun values ->
    Ok { Label = label; OnChange = onChange; OnChangeMulti = onChangeMulti; Source = source; Value = value; Placeholder = placeholder; Disabled = disabled; Multiple = multiple; Values = values }))))))))))

and private decFileUploadSpec (j: JVal) : Result<FileUploadSpec, string> =
    dObj j |> Result.bind (fun __fs ->
    dReq "accept" __fs (dList dStr) |> Result.bind (fun accept ->
    dReq "label" __fs decTextSource |> Result.bind (fun label ->
    dReq "multiple" __fs dBool |> Result.bind (fun multiple ->
    dPresent "onSelect" __fs |> Result.bind (fun onSelect ->
    dOpt "disabled" __fs (decBinding dBool) |> Result.bind (fun disabled ->
    Ok { Accept = accept; Label = label; Multiple = multiple; OnSelect = onSelect; Disabled = disabled }))))))

and private decFormSpec (j: JVal) : Result<FormSpec, string> =
    dObj j |> Result.bind (fun __fs ->
    dReq "fields" __fs (dList decFormField) |> Result.bind (fun fields ->
    dReq "onSubmit" __fs decAction |> Result.bind (fun onSubmit ->
    dReq "submitLabel" __fs decTextSource |> Result.bind (fun submitLabel ->
    dOpt "disabled" __fs (decBinding dBool) |> Result.bind (fun disabled ->
    Ok { Fields = fields; OnSubmit = onSubmit; SubmitLabel = submitLabel; Disabled = disabled })))))

and private decFiltersSpec (j: JVal) : Result<FiltersSpec, string> =
    dObj j |> Result.bind (fun __fs ->
    dReq "items" __fs (dList decFilterSpec) |> Result.bind (fun items ->
    Ok { Items = items }))

and private decDataGridSpec (j: JVal) : Result<DataGridSpec, string> =
    dObj j |> Result.bind (fun __fs ->
    dReq "columns" __fs (dList decColumnErased) |> Result.bind (fun columns ->
    dDef "editable" __fs dBool (false) |> Result.bind (fun editable ->
    dPresent "rowKey" __fs |> Result.bind (fun rowKey ->
    dReq "source" __fs (decBinding dUnit) |> Result.bind (fun source ->
    dOpt "staticRows" __fs decStaticRows |> Result.bind (fun staticRows ->
    dPresent "onRowClick" __fs |> Result.bind (fun onRowClick ->
    Ok { Columns = columns; Editable = editable; RowKey = rowKey; Source = source; StaticRows = staticRows; OnRowClick = onRowClick })))))))

and private decChartSpec (j: JVal) : Result<ChartSpec, string> =
    dObj j |> Result.bind (fun __fs ->
    dReq "kind" __fs decChartKind |> Result.bind (fun kind ->
    dReq "source" __fs (decBinding dUnit) |> Result.bind (fun source ->
    dReq "stacked" __fs dBool |> Result.bind (fun stacked ->
    dReq "xField" __fs dStr |> Result.bind (fun xField ->
    dReq "yFields" __fs (dList dStr) |> Result.bind (fun yFields ->
    dOpt "title" __fs decTextSource |> Result.bind (fun title ->
    dPresent "onPointClick" __fs |> Result.bind (fun onPointClick ->
    Ok { Kind = kind; Source = source; Stacked = stacked; XField = xField; YFields = yFields; Title = title; OnPointClick = onPointClick }))))))))

and private decMapSpec (j: JVal) : Result<MapSpec, string> =
    dObj j |> Result.bind (fun __fs ->
    dReq "centreLatitude" __fs dFloat |> Result.bind (fun centreLatitude ->
    dReq "centreLongitude" __fs dFloat |> Result.bind (fun centreLongitude ->
    dReq "source" __fs (decBinding (dList decMapMarker)) |> Result.bind (fun source ->
    dReq "zoom" __fs dInt |> Result.bind (fun zoom ->
    dPresent "onMarkerClick" __fs |> Result.bind (fun onMarkerClick ->
    Ok { CentreLatitude = centreLatitude; CentreLongitude = centreLongitude; Source = source; Zoom = zoom; OnMarkerClick = onMarkerClick }))))))

and private decCustomSpec (j: JVal) : Result<CustomSpec, string> =
    dObj j |> Result.bind (fun __fs ->
    dReq "moduleId" __fs dStr |> Result.bind (fun moduleId ->
    dReq "componentId" __fs dStr |> Result.bind (fun componentId ->
    dReq "props" __fs (dMap dUnit) |> Result.bind (fun props ->
    dOpt "contentHash" __fs decContentHash |> Result.bind (fun contentHash ->
    dOpt "exposedNodeIds" __fs (dList dStr) |> Result.bind (fun exposedNodeIds ->
    Ok { ModuleId = moduleId; ComponentId = componentId; Props = props; ContentHash = contentHash; ExposedNodeIds = exposedNodeIds }))))))

and private decErrorBoundarySpec (j: JVal) : Result<ErrorBoundarySpec, string> =
    dObj j |> Result.bind (fun __fs ->
    dReq "child" __fs decNode |> Result.bind (fun child ->
    dReq "fallback" __fs decNode |> Result.bind (fun fallback ->
    Ok { Child = child; Fallback = fallback })))

and private decFragmentDeclSpec (j: JVal) : Result<FragmentDeclSpec, string> =
    dObj j |> Result.bind (fun __fs ->
    dReq "body" __fs decNode |> Result.bind (fun body ->
    dReq "name" __fs dStr |> Result.bind (fun name ->
    dOpt "holes" __fs (dList decHoleDecl) |> Result.bind (fun holes ->
    dOpt "effect" __fs decEffectClass |> Result.bind (fun effect ->
    Ok { Body = body; Name = name; Holes = holes; Effect = effect })))))

and private decFragmentRefSpec (j: JVal) : Result<FragmentRefSpec, string> =
    dObj j |> Result.bind (fun __fs ->
    dReq "name" __fs dStr |> Result.bind (fun name ->
    dOpt "args" __fs (dMap decFragmentArg) |> Result.bind (fun args ->
    Ok { Name = name; Args = args })))

and private decSwitchSpec (j: JVal) : Result<SwitchSpec, string> =
    dObj j |> Result.bind (fun __fs ->
    dReq "cases" __fs (dList decSwitchCase) |> Result.bind (fun cases ->
    dReq "default" __fs decNode |> Result.bind (fun ``default`` ->
    dReq "stateKey" __fs dStr |> Result.bind (fun stateKey ->
    Ok { Cases = cases; Default = ``default``; StateKey = stateKey }))))

and private decMountSpec (j: JVal) : Result<MountSpec, string> =
    dObj j |> Result.bind (fun __fs ->
    dReq "capabilities" __fs (dList dStr) |> Result.bind (fun capabilities ->
    dReq "channel" __fs decGuestChannel |> Result.bind (fun channel ->
    dOpt "inputs" __fs (dMap decFragmentArg) |> Result.bind (fun inputs ->
    dPresent "onBubble" __fs |> Result.bind (fun onBubble ->
    dReq "scopeId" __fs dStr |> Result.bind (fun scopeId ->
    Ok { Capabilities = capabilities; Channel = channel; Inputs = inputs; OnBubble = onBubble; ScopeId = scopeId }))))))

/// Structural decode. The policy layer (diagnostics, §16 lenient-accept,
/// the reject set) composes ABOVE this — see the Phase 672 note in the generator.
let decodeNode (s: string) : Result<Node, string> =
    Json.parse s |> Result.bind decNode

let private witnessKindTag (n: Node) : string =
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

let private witnessChildren (n: Node) : Node list =
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

let private witnessReplaceChildren (n: Node) (kids: Node list) : Node =
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

let nodeWitness: NodeWitness<Node, string> =
    { Id = fun n -> n.Id
      KindTag = witnessKindTag
      Children = witnessChildren
      ReplaceChildren = witnessReplaceChildren }

// Validator scaffold — register domain RuleFamilies into `reg`; rule content stays domain-side.
let runValidator (reg: Validator.Registry<Node, string>) (root: Node) : Defect<string> list =
    Validator.runAll nodeWitness reg root

// Smart constructors — required-without-default fields are parameters; IDL-declared
// defaults are filled, other optionals default to None.

let mkHeading (id: string) (level: int) (text: TextSource) (variant: HeadingVariant) : Node =
    { Id = id; Kind = NodeKind.Heading { Level = level; Text = text; Variant = variant } }

let mkBadge (id: string) (label: TextSource) (variant: BadgeVariant) : Node =
    { Id = id; Kind = NodeKind.Badge { Label = label; Variant = variant } }

let mkMarkdown (id: string) (text: TextSource) : Node =
    { Id = id; Kind = NodeKind.Markdown { Text = text } }

let mkMath (id: string) (source: string) (display: MathDisplay) : Node =
    { Id = id; Kind = NodeKind.Math { Source = source; Display = display } }

let mkSkeleton (id: string) (rows: int) : Node =
    { Id = id; Kind = NodeKind.Skeleton { Rows = rows } }

let mkList (id: string) (items: TextSource list) (ordered: bool) : Node =
    { Id = id; Kind = NodeKind.List { Items = items; Ordered = ordered } }

let mkImage (id: string) (alt: TextSource) (src: Binding<string>) (variant: ImageVariant) : Node =
    { Id = id; Kind = NodeKind.Image { Alt = alt; Src = src; Variant = variant } }

let mkLink (id: string) (href: Binding<string>) (label: TextSource) (download: bool) : Node =
    { Id = id; Kind = NodeKind.Link { Href = href; Label = label; Download = download; Rel = None; Target = None } }

let mkCallout (id: string) (body: TextSource) : Node =
    { Id = id; Kind = NodeKind.Callout { Body = body; Dismissable = false; Tone = ToneVariant.Default; Heading = None; Icon = None } }

let mkProgress (id: string) (fraction: Binding<float>) : Node =
    { Id = id; Kind = NodeKind.Progress { Fraction = fraction; Indeterminate = false; Tone = ToneVariant.Default; Label = None; Caveat = None } }

let mkMetric (id: string) (label: TextSource) (value: Binding<float>) : Node =
    { Id = id; Kind = NodeKind.Metric { Label = label; Value = value; Format = CellFormat.None; Tone = ToneVariant.Default; Weight = StyleWeight.Standard; Emphasis = Emphasis.Normal; Trend = None; TrendFormat = None; Icon = None; Subtext = None } }

let mkLabelValueRow (id: string) (label: TextSource) (value: Binding<float>) : Node =
    { Id = id; Kind = NodeKind.LabelValueRow { Emphasis = false; Format = CellFormat.None; Label = label; Value = value; Help = None } }

let mkFact (id: string) (label: TextSource) (value: TextSource) : Node =
    { Id = id; Kind = NodeKind.Fact { Emphasis = false; Help = None; Icon = None; Label = label; Tone = ToneVariant.Default; Value = value } }

let mkSparkline (id: string) (source: Binding<int list>) : Node =
    { Id = id; Kind = NodeKind.Sparkline { Source = source } }

let mkCodeBlock (id: string) (code: string) (copyable: bool) (highlightLines: int list) (language: string) (lineNumbers: bool) : Node =
    { Id = id; Kind = NodeKind.CodeBlock { Code = code; Copyable = copyable; HighlightLines = highlightLines; Language = language; LineNumbers = lineNumbers } }

let mkToast (id: string) (message: TextSource) (``open``: Binding<bool>) : Node =
    { Id = id; Kind = NodeKind.Toast { Dismissable = true; Message = message; Open = ``open``; Tone = ToneVariant.Default } }

let mkDrawing (id: string) (shapes: Shape list) (style: DrawStyle) (viewBox: ViewBox) : Node =
    { Id = id; Kind = NodeKind.Drawing { Description = None; Shapes = shapes; Style = style; Title = None; ViewBox = viewBox } }

let mkBox (id: string) (children: Node list) (layout: LayoutMode) (role: BoxRole) : Node =
    { Id = id; Kind = NodeKind.Box { Children = children; Heading = None; Layout = layout; Role = role } }

let mkSplitPanel (id: string) (children: Node list) (weight: float) : Node =
    { Id = id; Kind = NodeKind.SplitPanel { Children = children; Weight = weight } }

let mkSummaryList (id: string) (children: Node list) : Node =
    { Id = id; Kind = NodeKind.SummaryList { Children = children; Heading = None } }

let mkDisclosure (id: string) (children: Node list) (defaultOpen: bool) (heading: TextSource) (``open``: Binding<bool>) : Node =
    { Id = id; Kind = NodeKind.Disclosure { Children = children; DefaultOpen = defaultOpen; Heading = heading; OnToggle = None; Open = ``open`` } }

let mkModal (id: string) (children: Node list) (dismissable: bool) (onDismiss: Action) (``open``: Binding<bool>) : Node =
    { Id = id; Kind = NodeKind.Modal { Children = children; Dismissable = dismissable; OnDismiss = onDismiss; Open = ``open``; Heading = None } }

let mkScrollArea (id: string) (children: Node list) (orientation: ScrollOrientation) : Node =
    { Id = id; Kind = NodeKind.ScrollArea { Children = children; Orientation = orientation; MaxHeight = None; MaxWidth = None } }

let mkTabs (id: string) (activeIndex: Binding<int>) (children: Node list) : Node =
    { Id = id; Kind = NodeKind.Tabs { ActiveIndex = activeIndex; Children = children; OnSelect = None; OnSelectTag = None; TabHeaders = None; TabTags = None; ActiveTag = None } }

let mkStepper (id: string) (activeStep: Binding<int>) (children: Node list) : Node =
    { Id = id; Kind = NodeKind.Stepper { ActiveStep = activeStep; Children = children; OnSelect = None } }

let mkButton (id: string) (label: TextSource) (onClick: Action) (variant: ButtonVariant) : Node =
    { Id = id; Kind = NodeKind.Button { Label = label; OnClick = onClick; Variant = variant; Icon = None; Tooltip = None; Disabled = None } }

let mkSelect (id: string) (label: TextSource) (source: Binding<SelectOption list>) (value: Binding<string>) : Node =
    { Id = id; Kind = NodeKind.Select { Label = label; OnChange = None; OnChangeMulti = None; Source = source; Value = value; Placeholder = None; Disabled = None; Multiple = None; Values = None } }

let mkFileUpload (id: string) (accept: string list) (label: TextSource) (multiple: bool) : Node =
    { Id = id; Kind = NodeKind.FileUpload { Accept = accept; Label = label; Multiple = multiple; OnSelect = None; Disabled = None } }

let mkForm (id: string) (fields: FormField list) (onSubmit: Action) (submitLabel: TextSource) : Node =
    { Id = id; Kind = NodeKind.Form { Fields = fields; OnSubmit = onSubmit; SubmitLabel = submitLabel; Disabled = None } }

let mkFilters (id: string) (items: FilterSpec list) : Node =
    { Id = id; Kind = NodeKind.Filters { Items = items } }

let mkDataGrid (id: string) (columns: ColumnErased list) (source: Binding<unit>) : Node =
    { Id = id; Kind = NodeKind.DataGrid { Columns = columns; Editable = false; RowKey = None; Source = source; StaticRows = None; OnRowClick = None } }

let mkChart (id: string) (kind: ChartKind) (source: Binding<unit>) (stacked: bool) (xField: string) (yFields: string list) : Node =
    { Id = id; Kind = NodeKind.Chart { Kind = kind; Source = source; Stacked = stacked; XField = xField; YFields = yFields; Title = None; OnPointClick = None } }

let mkMap (id: string) (centreLatitude: float) (centreLongitude: float) (source: Binding<MapMarker list>) (zoom: int) : Node =
    { Id = id; Kind = NodeKind.Map { CentreLatitude = centreLatitude; CentreLongitude = centreLongitude; Source = source; Zoom = zoom; OnMarkerClick = None } }

let mkCustom (id: string) (moduleId: string) (componentId: string) (props: Map<string, unit>) : Node =
    { Id = id; Kind = NodeKind.Custom { ModuleId = moduleId; ComponentId = componentId; Props = props; ContentHash = None; ExposedNodeIds = None } }

let mkErrorBoundary (id: string) (child: Node) (fallback: Node) : Node =
    { Id = id; Kind = NodeKind.ErrorBoundary { Child = child; Fallback = fallback } }

let mkFragmentDecl (id: string) (body: Node) (name: string) : Node =
    { Id = id; Kind = NodeKind.FragmentDecl { Body = body; Name = name; Holes = None; Effect = None } }

let mkFragmentRef (id: string) (name: string) : Node =
    { Id = id; Kind = NodeKind.FragmentRef { Name = name; Args = None } }

let mkSwitch (id: string) (cases: SwitchCase list) (``default``: Node) (stateKey: string) : Node =
    { Id = id; Kind = NodeKind.Switch { Cases = cases; Default = ``default``; StateKey = stateKey } }

let mkMount (id: string) (capabilities: string list) (channel: GuestChannel) (scopeId: string) : Node =
    { Id = id; Kind = NodeKind.Mount { Capabilities = capabilities; Channel = channel; Inputs = None; OnBubble = None; ScopeId = scopeId } }