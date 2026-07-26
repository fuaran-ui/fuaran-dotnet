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
type TextSource =
    | Literal of text: string
    | Bound of binding: Binding<string>

and [<RequireQualifiedAccess>] Binding<'T> =
    | Static of value: 'T
    | Query of accessor: unit * name: string
    | Filter of name: string
    | State of defaultValue: 'T * key: string
    | Computed of fn: unit
    | Local of flushOn: LocalFlushTrigger * format: unit * initialFrom: Binding<'T> * onCommit: unit * parse: unit
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
    | ReadFileBody of encoding: FileReadEncoding * fileRef: string * onRead: unit

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
    | Text of onChange: unit * value: Binding<string>
    | Number of onChange: unit * value: Binding<float>
    | Checkbox of onToggle: unit * value: Binding<bool>
    | Choice of onChange: unit * options: Binding<SelectOption list> * value: Binding<string>
    | TextArea of onChange: unit * rows: int * value: Binding<string>
    | RangedNumber of onChange: unit * value: Binding<float> * min: float option * max: float option * step: float option
    | SegmentedChoice of onChange: unit * options: Binding<SelectOption list> * orientation: Orientation * value: Binding<string>
    | Date of onChange: unit * value: Binding<string> * variant: DateVariant * min: string option * max: string option * step: float option

and [<RequireQualifiedAccess>] FilterKind =
    | Text of onChange: unit * value: Binding<string>
    | Choice of onChange: unit * options: Binding<SelectOption list> * value: Binding<string>
    | Range of onChange: unit * value: unit
    | SegmentedChoice of onChange: unit * options: Binding<SelectOption list> * orientation: Orientation * value: Binding<string>

and [<RequireQualifiedAccess>] ColumnWidth =
    | Auto
    | Fixed of pixels: int
    | Flex of weight: float

and [<RequireQualifiedAccess>] CellKindErased =
    | Text
    | Numeric
    | Date
    | Editable of onEdit: unit
    | Checkbox of get: unit * onToggle: unit
    | Button of label: TextSource * onClick: unit
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
      OnClick: unit
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
      OnSelect: unit
      TabHeaders: TabHeader list option
      TabTags: string list option
      ActiveTag: Binding<string> option
    }

// Layout
and StepperSpec =
    {
      ActiveStep: Binding<int>
      Children: Node list
      OnSelect: unit
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
      OnChange: unit
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
      OnSelect: unit
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
    | Sparkline of SparklineSpec
    | CodeBlock of CodeBlockSpec
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
        | NodeKind.Sparkline s -> encSparklineSpec s
        | NodeKind.CodeBlock s -> encCodeBlockSpec s
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

    JObj [ "id", JStr n.Id; "kind", kind ]

and private encTextSource (v: TextSource) : JVal =
    match v with
    | TextSource.Literal text -> JStr text
    | TextSource.Bound binding -> Canon.typed "Bound" [ "binding", (encBinding JStr) binding ]

and private encBinding<'T> (encT: 'T -> JVal) (v: Binding<'T>) : JVal =
    match v with
    | Binding.Static value -> Canon.typed "Static" [ "value", encT value ]
    | Binding.Query (accessor, name) -> Canon.typed "Query" [ "accessor", JStr "<closure>"; "name", JStr name ]
    | Binding.Filter name -> Canon.typed "Filter" [ "name", JStr name ]
    | Binding.State (defaultValue, key) -> Canon.typed "State" [ "defaultValue", encT defaultValue; "key", JStr key ]
    | Binding.Computed fn -> Canon.typed "Computed" [ "fn", JStr "<closure>" ]
    | Binding.Local (flushOn, format, initialFrom, onCommit, parse) -> Canon.typed "Local" [ "flushOn", encLocalFlushTrigger flushOn; "format", JStr "<closure>"; "initialFrom", (encBinding encT) initialFrom; "onCommit", JStr "<closure>"; "parse", JStr "<closure>" ]
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
    | Action.ReadFileBody (encoding, fileRef, onRead) -> Canon.typed "ReadFileBody" [ "encoding", encFileReadEncoding encoding; "fileRef", JStr fileRef; "onRead", JStr "<closure>" ]

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
    | FormFieldKind.Text (onChange, value) -> Canon.typed "Text" [ "onChange", JStr "<closure>"; "value", (encBinding JStr) value ]
    | FormFieldKind.Number (onChange, value) -> Canon.typed "Number" [ "onChange", JStr "<closure>"; "value", (encBinding JFloat) value ]
    | FormFieldKind.Checkbox (onToggle, value) -> Canon.typed "Checkbox" [ "onToggle", JStr "<closure>"; "value", (encBinding JBool) value ]
    | FormFieldKind.Choice (onChange, options, value) -> Canon.typed "Choice" [ "onChange", JStr "<closure>"; "options", (encBinding (fun __xs -> JArr(List.map encSelectOption __xs))) options; "value", (encBinding JStr) value ]
    | FormFieldKind.TextArea (onChange, rows, value) -> Canon.typed "TextArea" [ "onChange", JStr "<closure>"; "rows", JInt rows; "value", (encBinding JStr) value ]
    | FormFieldKind.RangedNumber (onChange, value, min, max, step) -> Canon.typed "RangedNumber" ([ Some("onChange", JStr "<closure>"); Some("value", (encBinding JFloat) value); (min |> Option.map (fun v -> "min", JFloat v)); (max |> Option.map (fun v -> "max", JFloat v)); (step |> Option.map (fun v -> "step", JFloat v)) ] |> List.choose id)
    | FormFieldKind.SegmentedChoice (onChange, options, orientation, value) -> Canon.typed "SegmentedChoice" [ "onChange", JStr "<closure>"; "options", (encBinding (fun __xs -> JArr(List.map encSelectOption __xs))) options; "orientation", encOrientation orientation; "value", (encBinding JStr) value ]
    | FormFieldKind.Date (onChange, value, variant, min, max, step) -> Canon.typed "Date" ([ Some("onChange", JStr "<closure>"); Some("value", (encBinding JStr) value); Some("variant", encDateVariant variant); (min |> Option.map (fun v -> "min", JStr v)); (max |> Option.map (fun v -> "max", JStr v)); (step |> Option.map (fun v -> "step", JFloat v)) ] |> List.choose id)

and private encFilterKind (v: FilterKind) : JVal =
    match v with
    | FilterKind.Text (onChange, value) -> Canon.typed "Text" [ "onChange", JStr "<closure>"; "value", (encBinding JStr) value ]
    | FilterKind.Choice (onChange, options, value) -> Canon.typed "Choice" [ "onChange", JStr "<closure>"; "options", (encBinding (fun __xs -> JArr(List.map encSelectOption __xs))) options; "value", (encBinding JStr) value ]
    | FilterKind.Range (onChange, value) -> Canon.typed "Range" [ "onChange", JStr "<closure>"; "value", JStr "<opaque>" ]
    | FilterKind.SegmentedChoice (onChange, options, orientation, value) -> Canon.typed "SegmentedChoice" [ "onChange", JStr "<closure>"; "options", (encBinding (fun __xs -> JArr(List.map encSelectOption __xs))) options; "orientation", encOrientation orientation; "value", (encBinding JStr) value ]

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
    | CellKindErased.Editable onEdit -> Canon.typed "Editable" [ "onEdit", JStr "<closure>" ]
    | CellKindErased.Checkbox (get, onToggle) -> Canon.typed "Checkbox" [ "get", JStr "<closure>"; "onToggle", JStr "<closure>" ]
    | CellKindErased.Button (label, onClick) -> Canon.typed "Button" [ "label", encTextSource label; "onClick", JStr "<closure>" ]
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
    JObj([ Some("label", encTextSource s.Label); Some("onClick", JStr "<closure>") ] |> List.choose id)

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

and private encSparklineSpec (s: SparklineSpec) : JVal =
    Canon.typed "Sparkline" ([ Some("source", (encBinding (fun __xs -> JArr(List.map JInt __xs))) s.Source) ] |> List.choose id)

and private encCodeBlockSpec (s: CodeBlockSpec) : JVal =
    Canon.typed "CodeBlock" ([ Some("code", JStr s.Code); Some("copyable", JBool s.Copyable); Some("highlightLines", JArr(List.map JInt s.HighlightLines)); Some("language", JStr s.Language); Some("lineNumbers", JBool s.LineNumbers) ] |> List.choose id)

and private encBoxSpec (s: BoxSpec) : JVal =
    Canon.typed "Box" ([ Some("children", JArr(List.map encNode s.Children)); (s.Heading |> Option.map (fun v -> "heading", encTextSource v)); Some("layout", encLayoutMode s.Layout); Some("role", encBoxRole s.Role) ] |> List.choose id)

and private encSplitPanelSpec (s: SplitPanelSpec) : JVal =
    Canon.typed "SplitPanel" ([ Some("children", JArr(List.map encNode s.Children)); Some("weight", JFloat s.Weight) ] |> List.choose id)

and private encSummaryListSpec (s: SummaryListSpec) : JVal =
    Canon.typed "SummaryList" ([ Some("children", JArr(List.map encNode s.Children)); (s.Heading |> Option.map (fun v -> "heading", encTextSource v)) ] |> List.choose id)

and private encDisclosureSpec (s: DisclosureSpec) : JVal =
    Canon.typed "Disclosure" ([ Some("children", JArr(List.map encNode s.Children)); Some("defaultOpen", JBool s.DefaultOpen); Some("heading", encTextSource s.Heading); Some("open", (encBinding JBool) s.Open) ] |> List.choose id)

and private encModalSpec (s: ModalSpec) : JVal =
    Canon.typed "Modal" ([ Some("children", JArr(List.map encNode s.Children)); Some("dismissable", JBool s.Dismissable); Some("onDismiss", encAction s.OnDismiss); Some("open", (encBinding JBool) s.Open); (s.Heading |> Option.map (fun v -> "heading", encTextSource v)) ] |> List.choose id)

and private encScrollAreaSpec (s: ScrollAreaSpec) : JVal =
    Canon.typed "ScrollArea" ([ Some("children", JArr(List.map encNode s.Children)); Some("orientation", encScrollOrientation s.Orientation); (s.MaxHeight |> Option.map (fun v -> "maxHeight", JInt v)); (s.MaxWidth |> Option.map (fun v -> "maxWidth", JInt v)) ] |> List.choose id)

and private encTabsSpec (s: TabsSpec) : JVal =
    Canon.typed "Tabs" ([ Some("activeIndex", (encBinding JInt) s.ActiveIndex); Some("children", JArr(List.map encNode s.Children)); Some("onSelect", JStr "<closure>"); (s.TabHeaders |> Option.map (fun v -> "tabHeaders", JArr(List.map encTabHeader v))); (s.TabTags |> Option.map (fun v -> "tabTags", JArr(List.map JStr v))); (s.ActiveTag |> Option.map (fun v -> "activeTag", (encBinding JStr) v)) ] |> List.choose id)

and private encStepperSpec (s: StepperSpec) : JVal =
    Canon.typed "Stepper" ([ Some("activeStep", (encBinding JInt) s.ActiveStep); Some("children", JArr(List.map encNode s.Children)); Some("onSelect", JStr "<closure>") ] |> List.choose id)

and private encButtonSpec (s: ButtonSpec) : JVal =
    Canon.typed "Button" ([ Some("label", encTextSource s.Label); Some("onClick", encAction s.OnClick); Some("variant", encButtonVariant s.Variant); (s.Icon |> Option.map (fun v -> "icon", JStr v)); (s.Tooltip |> Option.map (fun v -> "tooltip", encTextSource v)); (s.Disabled |> Option.map (fun v -> "disabled", (encBinding JBool) v)) ] |> List.choose id)

and private encSelectSpec (s: SelectSpec) : JVal =
    Canon.typed "Select" ([ Some("label", encTextSource s.Label); Some("onChange", JStr "<closure>"); Some("source", (encBinding (fun __xs -> JArr(List.map encSelectOption __xs))) s.Source); Some("value", (encBinding JStr) s.Value); (s.Placeholder |> Option.map (fun v -> "placeholder", encTextSource v)); (s.Disabled |> Option.map (fun v -> "disabled", (encBinding JBool) v)); (s.Multiple |> Option.map (fun v -> "multiple", JBool v)); (s.Values |> Option.map (fun v -> "values", (encBinding (fun __xs -> JArr(List.map JStr __xs))) v)) ] |> List.choose id)

and private encFileUploadSpec (s: FileUploadSpec) : JVal =
    Canon.typed "FileUpload" ([ Some("accept", JArr(List.map JStr s.Accept)); Some("label", encTextSource s.Label); Some("multiple", JBool s.Multiple); Some("onSelect", JStr "<closure>"); (s.Disabled |> Option.map (fun v -> "disabled", (encBinding JBool) v)) ] |> List.choose id)

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

let encodeNode (n: Node) : string = Canon.render (encNode n)

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
    | NodeKind.Sparkline _ -> "Sparkline"
    | NodeKind.CodeBlock _ -> "CodeBlock"
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

let mkSparkline (id: string) (source: Binding<int list>) : Node =
    { Id = id; Kind = NodeKind.Sparkline { Source = source } }

let mkCodeBlock (id: string) (code: string) (copyable: bool) (highlightLines: int list) (language: string) (lineNumbers: bool) : Node =
    { Id = id; Kind = NodeKind.CodeBlock { Code = code; Copyable = copyable; HighlightLines = highlightLines; Language = language; LineNumbers = lineNumbers } }

let mkBox (id: string) (children: Node list) (layout: LayoutMode) (role: BoxRole) : Node =
    { Id = id; Kind = NodeKind.Box { Children = children; Heading = None; Layout = layout; Role = role } }

let mkSplitPanel (id: string) (children: Node list) (weight: float) : Node =
    { Id = id; Kind = NodeKind.SplitPanel { Children = children; Weight = weight } }

let mkSummaryList (id: string) (children: Node list) : Node =
    { Id = id; Kind = NodeKind.SummaryList { Children = children; Heading = None } }

let mkDisclosure (id: string) (children: Node list) (defaultOpen: bool) (heading: TextSource) (``open``: Binding<bool>) : Node =
    { Id = id; Kind = NodeKind.Disclosure { Children = children; DefaultOpen = defaultOpen; Heading = heading; Open = ``open`` } }

let mkModal (id: string) (children: Node list) (dismissable: bool) (onDismiss: Action) (``open``: Binding<bool>) : Node =
    { Id = id; Kind = NodeKind.Modal { Children = children; Dismissable = dismissable; OnDismiss = onDismiss; Open = ``open``; Heading = None } }

let mkScrollArea (id: string) (children: Node list) (orientation: ScrollOrientation) : Node =
    { Id = id; Kind = NodeKind.ScrollArea { Children = children; Orientation = orientation; MaxHeight = None; MaxWidth = None } }

let mkTabs (id: string) (activeIndex: Binding<int>) (children: Node list) (onSelect: unit) : Node =
    { Id = id; Kind = NodeKind.Tabs { ActiveIndex = activeIndex; Children = children; OnSelect = onSelect; TabHeaders = None; TabTags = None; ActiveTag = None } }

let mkStepper (id: string) (activeStep: Binding<int>) (children: Node list) (onSelect: unit) : Node =
    { Id = id; Kind = NodeKind.Stepper { ActiveStep = activeStep; Children = children; OnSelect = onSelect } }

let mkButton (id: string) (label: TextSource) (onClick: Action) (variant: ButtonVariant) : Node =
    { Id = id; Kind = NodeKind.Button { Label = label; OnClick = onClick; Variant = variant; Icon = None; Tooltip = None; Disabled = None } }

let mkSelect (id: string) (label: TextSource) (onChange: unit) (source: Binding<SelectOption list>) (value: Binding<string>) : Node =
    { Id = id; Kind = NodeKind.Select { Label = label; OnChange = onChange; Source = source; Value = value; Placeholder = None; Disabled = None; Multiple = None; Values = None } }

let mkFileUpload (id: string) (accept: string list) (label: TextSource) (multiple: bool) (onSelect: unit) : Node =
    { Id = id; Kind = NodeKind.FileUpload { Accept = accept; Label = label; Multiple = multiple; OnSelect = onSelect; Disabled = None } }

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