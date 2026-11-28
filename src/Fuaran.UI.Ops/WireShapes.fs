// GENERATED from src/Fuaran.UI.Idl/idl.json by src/Fuaran.UI.Idl/WireShapes.fs. Do not edit by hand:
// FUARAN_REGEN=1 dotnet run --project src/Fuaran.UI.Idl.Tests rewrites it, and the same suite fails
// when it drifts. The declared wire members of every object shape (WIRE_FORMAT §15.8, Phase 2880).
module Fuaran.UI.Ops.WireShapes

/// What a declared member's value is, as far as finding the objects inside it goes.
[<RequireQualifiedAccess>]
type Shape =
    /// A scalar, an enum token, a closure sentinel or a payload carried whole: nothing to walk.
    | Leaf
    /// A node: its own members are its own.
    | Node
    /// A node kind, chosen by `$type` from `kinds`.
    | Kind
    /// A tree op, chosen by `$type` from `ops`.
    | Op
    | Record of string
    /// A union, its case chosen by `$type`.
    | Union of string
    | List of Shape
    /// A map type: its keys are entries, never members.
    | Map of Shape

/// The node envelope's wire members.
let node: (string * Shape) list = [ "accessibility", Shape.Record "Accessibility"; "extraAttributes", Shape.Leaf; "fallback", Shape.Node; "id", Shape.Leaf; "keptMembers", Shape.Leaf; "kind", Shape.Kind; "motion", Shape.Leaf; "state", Shape.Record "StateBehaviour"; "style", Shape.Record "SemanticStyle"; "tooltip", Shape.Union "TextSource"; "visible", Shape.Union "Binding" ]

/// Each node kind's wire members, by `$type`.
let kinds: Map<string, (string * Shape) list> =
    Map.ofList
        [
            "Badge", [ "$type", Shape.Leaf; "label", Shape.Union "TextSource"; "variant", Shape.Leaf ]
            "Box", [ "$type", Shape.Leaf; "breakBefore", Shape.Leaf; "children", Shape.List(Shape.Node); "heading", Shape.Union "TextSource"; "keepTogether", Shape.Leaf; "layout", Shape.Union "BoxLayout"; "role", Shape.Leaf ]
            "Button", [ "$type", Shape.Leaf; "affordance", Shape.Record "Affordance"; "disabled", Shape.Union "Binding"; "icon", Shape.Leaf; "label", Shape.Union "TextSource"; "onClick", Shape.Union "Action"; "tooltip", Shape.Leaf; "variant", Shape.Leaf ]
            "Callout", [ "$type", Shape.Leaf; "body", Shape.Union "TextSource"; "dismissable", Shape.Leaf; "heading", Shape.Union "TextSource"; "icon", Shape.Leaf; "tone", Shape.Leaf ]
            "Chart", [ "$type", Shape.Leaf; "annotations", Shape.List(Shape.Union "ChartAnnotation"); "dataLabels", Shape.Leaf; "interval", Shape.Record "ChartInterval"; "kind", Shape.Leaf; "legendPosition", Shape.Leaf; "onPointClick", Shape.Leaf; "pathField", Shape.Leaf; "scale", Shape.Union "ChartScale"; "sizeField", Shape.Leaf; "sortBy", Shape.Record "ChartSort"; "source", Shape.Union "Binding"; "stacked", Shape.Leaf; "subtitle", Shape.Union "TextSource"; "title", Shape.Union "TextSource"; "valueFormat", Shape.Union "Format"; "withheldField", Shape.Leaf; "xField", Shape.Leaf; "xScale", Shape.Leaf; "xTitle", Shape.Union "TextSource"; "yFields", Shape.List(Shape.Leaf); "yTitle", Shape.Union "TextSource" ]
            "CodeBlock", [ "$type", Shape.Leaf; "code", Shape.Leaf; "copyable", Shape.Leaf; "highlightLines", Shape.List(Shape.Leaf); "language", Shape.Leaf; "lineNumbers", Shape.Leaf ]
            "Custom", [ "$type", Shape.Leaf; "componentId", Shape.Leaf; "contentHash", Shape.Record "ContentHash"; "exposedNodeIds", Shape.List(Shape.Leaf); "moduleId", Shape.Leaf; "props", Shape.Map(Shape.Leaf) ]
            "DataGrid", [ "$type", Shape.Leaf; "columns", Shape.List(Shape.Record "ColumnErased"); "defaultSort", Shape.Record "DefaultSort"; "editStateKey", Shape.Leaf; "editable", Shape.Leaf; "exportable", Shape.Leaf; "keepRowsTogether", Shape.Leaf; "onRowClick", Shape.Leaf; "pageSize", Shape.Leaf; "pageStateKey", Shape.Leaf; "reorderable", Shape.Leaf; "repeatHeader", Shape.Leaf; "rowKey", Shape.Leaf; "rowKeyField", Shape.Leaf; "rowTotal", Shape.Union "Binding"; "sortStateKey", Shape.Leaf; "source", Shape.Union "Binding"; "staticRows", Shape.Record "StaticRows"; "transferInKey", Shape.Leaf; "transferOutKey", Shape.Leaf; "windowStateKey", Shape.Leaf ]
            "Disclosure", [ "$type", Shape.Leaf; "children", Shape.List(Shape.Node); "defaultOpen", Shape.Leaf; "heading", Shape.Union "TextSource"; "onToggle", Shape.Leaf; "open", Shape.Union "Binding" ]
            "Drawing", [ "$type", Shape.Leaf; "description", Shape.Union "TextSource"; "shapes", Shape.List(Shape.Union "Shape"); "style", Shape.Record "DrawStyle"; "title", Shape.Union "TextSource"; "viewBox", Shape.Record "ViewBox" ]
            "Embed", [ "$type", Shape.Leaf; "aspectRatio", Shape.Leaf; "permissions", Shape.List(Shape.Leaf); "src", Shape.Union "Binding"; "title", Shape.Union "TextSource" ]
            "ErrorBoundary", [ "$type", Shape.Leaf; "child", Shape.Node; "fallback", Shape.Node ]
            "Fact", [ "$type", Shape.Leaf; "emphasis", Shape.Leaf; "help", Shape.Union "TextSource"; "icon", Shape.Leaf; "label", Shape.Union "TextSource"; "reference", Shape.Leaf; "tone", Shape.Leaf; "value", Shape.Union "TextSource" ]
            "FileUpload", [ "$type", Shape.Leaf; "accept", Shape.List(Shape.Leaf); "acceptPaste", Shape.Leaf; "capture", Shape.Leaf; "destination", Shape.Leaf; "disabled", Shape.Union "Binding"; "dropTarget", Shape.Leaf; "label", Shape.Union "TextSource"; "maxBytes", Shape.Leaf; "maxFiles", Shape.Leaf; "multiple", Shape.Leaf; "onSelect", Shape.Leaf ]
            "Filters", [ "$type", Shape.Leaf; "items", Shape.List(Shape.Record "FilterSpec") ]
            "Form", [ "$type", Shape.Leaf; "disabled", Shape.Union "Binding"; "fields", Shape.List(Shape.Record "FormField"); "onSubmit", Shape.Union "Action"; "submitLabel", Shape.Union "TextSource" ]
            "FragmentDecl", [ "$type", Shape.Leaf; "body", Shape.Node; "effect", Shape.Record "EffectClass"; "holes", Shape.List(Shape.Union "HoleDecl"); "name", Shape.Leaf ]
            "FragmentRef", [ "$type", Shape.Leaf; "args", Shape.Map(Shape.Union "FragmentArg"); "name", Shape.Leaf ]
            "Heading", [ "$type", Shape.Leaf; "level", Shape.Leaf; "text", Shape.Union "TextSource"; "variant", Shape.Leaf ]
            "Icon", [ "$type", Shape.Leaf; "icon", Shape.Leaf; "label", Shape.Leaf; "size", Shape.Leaf; "tone", Shape.Leaf ]
            "Image", [ "$type", Shape.Leaf; "alt", Shape.Union "TextSource"; "aspectRatio", Shape.Leaf; "caption", Shape.Union "TextSource"; "expandable", Shape.Leaf; "fit", Shape.Leaf; "loading", Shape.Leaf; "src", Shape.Union "Binding"; "srcSet", Shape.List(Shape.Record "SrcSetEntry"); "variant", Shape.Leaf ]
            "LabelValueRow", [ "$type", Shape.Leaf; "emphasis", Shape.Leaf; "format", Shape.Union "CellFormat"; "help", Shape.Union "TextSource"; "label", Shape.Union "TextSource"; "value", Shape.Union "Binding" ]
            "Link", [ "$type", Shape.Leaf; "download", Shape.Leaf; "href", Shape.Union "Binding"; "label", Shape.Union "TextSource"; "protection", Shape.Leaf; "rel", Shape.Leaf; "target", Shape.Leaf ]
            "List", [ "$type", Shape.Leaf; "items", Shape.List(Shape.Union "TextSource"); "ordered", Shape.Leaf ]
            "Map", [ "$type", Shape.Leaf; "centreLatitude", Shape.Leaf; "centreLongitude", Shape.Leaf; "onMarkerClick", Shape.Leaf; "source", Shape.Union "Binding"; "zoom", Shape.Leaf ]
            "Markdown", [ "$type", Shape.Leaf; "citations", Shape.List(Shape.Record "MarkdownCitation"); "text", Shape.Union "TextSource" ]
            "Math", [ "$type", Shape.Leaf; "display", Shape.Leaf; "source", Shape.Leaf ]
            "Media", [ "$type", Shape.Leaf; "controls", Shape.Leaf; "kind", Shape.Union "MediaKind"; "label", Shape.Union "TextSource"; "loop", Shape.Leaf; "src", Shape.Union "Binding"; "tracks", Shape.List(Shape.Record "TrackEntry"); "transcript", Shape.Union "TextSource" ]
            "Metric", [ "$type", Shape.Leaf; "comparisons", Shape.List(Shape.Record "MetricComparison"); "emphasis", Shape.Leaf; "format", Shape.Union "CellFormat"; "icon", Shape.Leaf; "label", Shape.Union "TextSource"; "subtext", Shape.Union "TextSource"; "tone", Shape.Leaf; "trend", Shape.Union "Binding"; "trendFormat", Shape.Union "CellFormat"; "trendPolarity", Shape.Leaf; "value", Shape.Union "Binding"; "weight", Shape.Leaf ]
            "Modal", [ "$type", Shape.Leaf; "anchor", Shape.Leaf; "children", Shape.List(Shape.Node); "dismissable", Shape.Leaf; "heading", Shape.Union "TextSource"; "modality", Shape.Leaf; "onDismiss", Shape.Union "Action"; "open", Shape.Union "Binding" ]
            "Mount", [ "$type", Shape.Leaf; "capabilities", Shape.List(Shape.Leaf); "channel", Shape.Record "GuestChannel"; "inputs", Shape.Map(Shape.Union "FragmentArg"); "onBubble", Shape.Leaf; "scopeId", Shape.Leaf ]
            "Progress", [ "$type", Shape.Leaf; "caveat", Shape.Union "TextSource"; "fraction", Shape.Union "Binding"; "indeterminate", Shape.Leaf; "label", Shape.Union "TextSource"; "tone", Shape.Leaf ]
            "ScrollArea", [ "$type", Shape.Leaf; "children", Shape.List(Shape.Node); "maxHeight", Shape.Leaf; "maxWidth", Shape.Leaf; "orientation", Shape.Leaf ]
            "Select", [ "$type", Shape.Leaf; "affordance", Shape.Record "Affordance"; "disabled", Shape.Union "Binding"; "groups", Shape.List(Shape.Record "SelectOptionGroup"); "label", Shape.Union "TextSource"; "multiple", Shape.Leaf; "onChange", Shape.Leaf; "onChangeMulti", Shape.Leaf; "placeholder", Shape.Union "TextSource"; "source", Shape.Union "Binding"; "value", Shape.Union "Binding"; "values", Shape.Union "Binding" ]
            "Skeleton", [ "$type", Shape.Leaf; "rows", Shape.Leaf ]
            "Sparkline", [ "$type", Shape.Leaf; "source", Shape.Union "Binding" ]
            "SplitPanel", [ "$type", Shape.Leaf; "children", Shape.List(Shape.Node); "weight", Shape.Leaf ]
            "Stepper", [ "$type", Shape.Leaf; "activeStep", Shape.Union "Binding"; "children", Shape.List(Shape.Node); "onSelect", Shape.Leaf ]
            "SummaryList", [ "$type", Shape.Leaf; "children", Shape.List(Shape.Node); "heading", Shape.Union "TextSource" ]
            "Switch", [ "$type", Shape.Leaf; "autoAdvanceMs", Shape.Leaf; "cases", Shape.List(Shape.Record "SwitchCase"); "default", Shape.Node; "on", Shape.Union "Binding"; "stateKey", Shape.Leaf ]
            "Tabs", [ "$type", Shape.Leaf; "activeIndex", Shape.Union "Binding"; "activeTag", Shape.Union "Binding"; "children", Shape.List(Shape.Node); "onSelect", Shape.Leaf; "onSelectTag", Shape.Leaf; "orientation", Shape.Leaf; "tabHeaders", Shape.List(Shape.Record "TabHeader"); "tabTags", Shape.List(Shape.Leaf) ]
            "Text", [ "$type", Shape.Leaf; "text", Shape.Union "TextSource" ]
            "Toast", [ "$type", Shape.Leaf; "dismissable", Shape.Leaf; "message", Shape.Union "TextSource"; "open", Shape.Union "Binding"; "tone", Shape.Leaf ]
            "Tree", [ "$type", Shape.Leaf; "expandedStateKey", Shape.Leaf; "items", Shape.List(Shape.Record "TreeItem"); "onSelect", Shape.Leaf; "selectionStateKey", Shape.Leaf ]
        ]

/// Each record's wire members, by name.
let records: Map<string, (string * Shape) list> =
    Map.ofList
        [
            "Accessibility", [ "describedBy", Shape.Leaf; "hidden", Shape.Union "Binding"; "label", Shape.Union "Binding"; "labelledBy", Shape.Leaf; "liveRegion", Shape.Leaf; "role", Shape.Leaf; "speak", Shape.Union "TextSource" ]
            "Affordance", [ "aliases", Shape.List(Shape.Record "AffordanceAlias"); "clearable", Shape.Leaf; "description", Shape.Leaf; "neverControllable", Shape.Leaf; "patterns", Shape.List(Shape.Leaf); "readPatterns", Shape.List(Shape.Leaf) ]
            "AffordanceAlias", [ "alias", Shape.Leaf; "value", Shape.Leaf ]
            "ButtonGroupItem", [ "label", Shape.Union "TextSource"; "onClick", Shape.Leaf ]
            "ChartInterval", [ "high", Shape.Leaf; "low", Shape.Leaf ]
            "ChartSort", [ "direction", Shape.Leaf; "field", Shape.Leaf ]
            "ColumnErased", [ "editable", Shape.Leaf; "field", Shape.Leaf; "format", Shape.Union "CellFormat"; "kind", Shape.Union "CellKindErased"; "label", Shape.Leaf; "sortable", Shape.Leaf; "value", Shape.Leaf; "width", Shape.Union "ColumnWidth" ]
            "CompareRule", [ "against", Shape.Union "Binding"; "op", Shape.Leaf ]
            "ContentHash", [ "algorithm", Shape.Leaf; "hash", Shape.Leaf; "strictness", Shape.Leaf ]
            "DateTimeRangePair", [ "from", Shape.Leaf; "to", Shape.Leaf ]
            "DefaultSort", [ "column", Shape.Leaf; "direction", Shape.Leaf ]
            "DrawPoint", [ "x", Shape.Leaf; "y", Shape.Leaf ]
            "DrawStyle", [ "emphasis", Shape.Leaf; "fill", Shape.Union "Binding"; "fontFamily", Shape.Leaf; "fontSize", Shape.Leaf; "markId", Shape.Leaf; "opacity", Shape.Union "Binding"; "rotation", Shape.Leaf; "stroke", Shape.Union "Binding"; "strokeWidth", Shape.Union "Binding"; "textAnchor", Shape.Leaf; "tip", Shape.Union "TextSource" ]
            "EffectClass", [ "determinism", Shape.Leaf; "hostEffect", Shape.Leaf ]
            "FieldRule", [ "compare", Shape.Record "CompareRule"; "format", Shape.Leaf; "maxLength", Shape.Leaf; "message", Shape.Union "TextSource"; "minLength", Shape.Leaf; "pattern", Shape.Leaf ]
            "FilterSpec", [ "kind", Shape.Union "FormFieldKind"; "label", Shape.Union "TextSource"; "name", Shape.Leaf ]
            "FormField", [ "affordance", Shape.Record "Affordance"; "help", Shape.Union "TextSource"; "id", Shape.Leaf; "kind", Shape.Union "FormFieldKind"; "label", Shape.Union "TextSource"; "required", Shape.Leaf; "rule", Shape.Record "FieldRule" ]
            "GuestChannel", [ "direction", Shape.Leaf; "messageShape", Shape.Leaf ]
            "InvokeArg", [ "addr", Shape.Leaf; "value", Shape.Leaf ]
            "MapMarker", [ "label", Shape.Union "TextSource"; "latitude", Shape.Leaf; "longitude", Shape.Leaf ]
            "MarkdownCitation", [ "label", Shape.Union "TextSource"; "name", Shape.Leaf; "reference", Shape.Leaf ]
            "MetricComparison", [ "format", Shape.Union "CellFormat"; "label", Shape.Union "TextSource"; "polarity", Shape.Leaf; "value", Shape.Union "Binding" ]
            "RangePair", [ "max", Shape.Leaf; "min", Shape.Leaf ]
            "SelectOption", [ "label", Shape.Union "TextSource"; "value", Shape.Leaf ]
            "SelectOptionGroup", [ "label", Shape.Leaf; "values", Shape.List(Shape.Leaf) ]
            "SemanticStyle", [ "direction", Shape.Leaf; "emphasis", Shape.Leaf; "role", Shape.Leaf; "tone", Shape.Leaf; "voice", Shape.Leaf; "weight", Shape.Leaf ]
            "SrcSetEntry", [ "src", Shape.Union "Binding"; "width", Shape.Leaf ]
            "StateBehaviour", [ "onEmpty", Shape.Node; "onError", Shape.Leaf; "onLoading", Shape.Node ]
            "StaticRows", [ "citations", Shape.List(Shape.Record "TableCitation"); "defaultSort", Shape.Record "DefaultSort"; "headers", Shape.List(Shape.Union "TextSource"); "rows", Shape.List(Shape.List(Shape.Union "TextSource")); "sortable", Shape.Leaf ]
            "StyleDelta", [ "direction", Shape.Leaf; "emphasis", Shape.Leaf; "role", Shape.Leaf; "tone", Shape.Leaf; "voice", Shape.Leaf; "weight", Shape.Leaf ]
            "SwitchCase", [ "child", Shape.Node; "match", Shape.Leaf; "when", Shape.Union "Binding" ]
            "TabHeader", [ "disabled", Shape.Union "Binding"; "icon", Shape.Leaf; "label", Shape.Union "TextSource" ]
            "TableCitation", [ "column", Shape.Leaf; "label", Shape.Union "TextSource"; "reference", Shape.Leaf; "row", Shape.Leaf ]
            "TrackEntry", [ "default", Shape.Leaf; "kind", Shape.Leaf; "label", Shape.Union "TextSource"; "src", Shape.Union "Binding"; "srcLang", Shape.Leaf ]
            "TransformParam", [ "from", Shape.Union "Binding"; "name", Shape.Leaf ]
            "TreeItem", [ "children", Shape.List(Shape.Record "TreeItem"); "icon", Shape.Leaf; "id", Shape.Leaf; "label", Shape.Union "TextSource" ]
            "ViewBox", [ "height", Shape.Leaf; "minX", Shape.Leaf; "minY", Shape.Leaf; "width", Shape.Leaf ]
        ]

/// Each tree op's wire members, by `$type`.
let ops: Map<string, (string * Shape) list> =
    Map.ofList
        [
            "Batch", [ "$type", Shape.Leaf; "ops", Shape.List(Shape.Op) ]
            "EditNode", [ "$type", Shape.Leaf; "newKind", Shape.Kind; "target", Shape.Leaf ]
            "InsertChild", [ "$type", Shape.Leaf; "child", Shape.Node; "parentId", Shape.Leaf ]
            "MoveNode", [ "$type", Shape.Leaf; "newParentId", Shape.Leaf; "target", Shape.Leaf ]
            "PatchStyle", [ "$type", Shape.Leaf; "style", Shape.Record "StyleDelta"; "target", Shape.Leaf ]
            "RemoveNode", [ "$type", Shape.Leaf; "target", Shape.Leaf ]
            "ReorderChildren", [ "$type", Shape.Leaf; "newOrder", Shape.List(Shape.Leaf); "parentId", Shape.Leaf ]
            "ReplaceBinding", [ "$type", Shape.Leaf; "binding", Shape.Union "Binding"; "slot", Shape.Leaf; "target", Shape.Leaf ]
            "ReplaceRoot", [ "$type", Shape.Leaf; "node", Shape.Node ]
            "UpdateAccessibility", [ "$type", Shape.Leaf; "accessibility", Shape.Record "Accessibility"; "target", Shape.Leaf ]
            "UpdateProp", [ "$type", Shape.Leaf; "path", Shape.Leaf; "target", Shape.Leaf; "value", Shape.Leaf ]
            "UpdateState", [ "$type", Shape.Leaf; "state", Shape.Record "StateBehaviour"; "target", Shape.Leaf ]
            "UpdateStyle", [ "$type", Shape.Leaf; "style", Shape.Record "SemanticStyle"; "target", Shape.Leaf ]
            "UpdateTooltip", [ "$type", Shape.Leaf; "target", Shape.Leaf; "tooltip", Shape.Union "TextSource" ]
        ]

/// Each union's cases, by `$type`, and each case's wire members.
let unions: Map<string, Map<string, (string * Shape) list>> =
    Map.ofList
        [
            "Action",
            Map.ofList
                [
                    "AiTool", [ "$type", Shape.Leaf; "args", Shape.Leaf; "toolName", Shape.Leaf ]
                    "Call", [ "$type", Shape.Leaf; "endpoint", Shape.Leaf; "into", Shape.Union "CallResultTarget"; "onResult", Shape.Leaf ]
                    "Chain", [ "$type", Shape.Leaf; "ops", Shape.List(Shape.Union "Action") ]
                    "Choose", [ "$type", Shape.Leaf; "condition", Shape.Union "Binding"; "exit", Shape.Union "Binding"; "whenFalse", Shape.Union "Action"; "whenTrue", Shape.Union "Action" ]
                    "CommitLocal", [ "$type", Shape.Leaf; "nodeId", Shape.Leaf ]
                    "Confirm", [ "$type", Shape.Leaf; "onCancel", Shape.Union "Action"; "onConfirm", Shape.Union "Action"; "prompt", Shape.Union "TextSource" ]
                    "Dispatch", [ "$type", Shape.Leaf; "msg", Shape.Leaf ]
                    "Focus", [ "$type", Shape.Leaf; "nodeId", Shape.Leaf ]
                    "Invoke", [ "$type", Shape.Leaf; "args", Shape.List(Shape.Record "InvokeArg"); "capabilityId", Shape.Leaf ]
                    "Navigate", [ "$type", Shape.Leaf; "route", Shape.Union "TextSource"; "target", Shape.Leaf ]
                    "Notify", [ "$type", Shape.Leaf; "channel", Shape.Leaf; "payload", Shape.Leaf ]
                    "Print", [ "$type", Shape.Leaf ]
                    "ReadFileBody", [ "$type", Shape.Leaf; "encoding", Shape.Leaf; "fileHandle", Shape.Leaf; "fileRef", Shape.Leaf; "onRead", Shape.Leaf ]
                    "Require", [ "$type", Shape.Leaf; "condition", Shape.Union "Binding" ]
                    "SetState", [ "$type", Shape.Leaf; "key", Shape.Leaf; "value", Shape.Leaf; "valueFrom", Shape.Union "Binding" ]
                    "WriteToClipboard", [ "$type", Shape.Leaf; "text", Shape.Union "TextSource" ]
                ]
            "Binding",
            Map.ofList
                [
                    "Computed", [ "$type", Shape.Leaf; "fn", Shape.Leaf ]
                    "Expr", [ "$type", Shape.Leaf; "expr", Shape.Leaf; "params", Shape.List(Shape.Record "TransformParam") ]
                    "Filter", [ "$type", Shape.Leaf; "defaultValue", Shape.Leaf; "name", Shape.Leaf ]
                    "Format", [ "$type", Shape.Leaf; "format", Shape.Union "Format"; "locale", Shape.Union "LocaleSource"; "source", Shape.Union "Binding" ]
                    "I18n", [ "$type", Shape.Leaf; "args", Shape.Map(Shape.Union "Binding"); "key", Shape.Leaf ]
                    "Invoke", [ "$type", Shape.Leaf; "args", Shape.List(Shape.Record "InvokeArg"); "capabilityId", Shape.Leaf ]
                    "Local", [ "$type", Shape.Leaf; "codec", Shape.Union "Format"; "commitTo", Shape.Leaf; "flushOn", Shape.Union "LocalFlushTrigger"; "format", Shape.Leaf; "initialFrom", Shape.Union "Binding"; "onCommit", Shape.Leaf; "parse", Shape.Leaf ]
                    "Now", [ "$type", Shape.Leaf; "accessor", Shape.Leaf; "grain", Shape.Leaf ]
                    "Query", [ "$type", Shape.Leaf; "accessor", Shape.Leaf; "dependsOn", Shape.List(Shape.Leaf); "name", Shape.Leaf ]
                    "Selection", [ "$type", Shape.Leaf; "accessor", Shape.Leaf; "defaultValue", Shape.Leaf; "field", Shape.Leaf; "nodeId", Shape.Leaf ]
                    "State", [ "$type", Shape.Leaf; "defaultValue", Shape.Leaf; "key", Shape.Leaf ]
                    "Static", [ "$type", Shape.Leaf; "value", Shape.Leaf ]
                    "Transform", [ "$type", Shape.Leaf; "params", Shape.List(Shape.Record "TransformParam"); "pipeline", Shape.List(Shape.Leaf); "source", Shape.Leaf ]
                ]
            "BoxLayout",
            Map.ofList
                [
                    "Auto", [ "$type", Shape.Leaf ]
                    "Flex", [ "$type", Shape.Leaf; "direction", Shape.Leaf; "gap", Shape.Leaf; "wrap", Shape.Leaf ]
                    "Grid", [ "$type", Shape.Leaf; "cols", Shape.Leaf; "gap", Shape.Leaf; "templateColumns", Shape.Leaf ]
                    "Masonry", [ "$type", Shape.Leaf; "cols", Shape.Leaf; "gap", Shape.Leaf ]
                ]
            "CallResultTarget",
            Map.ofList
                [
                    "Query", [ "$type", Shape.Leaf; "name", Shape.Leaf ]
                    "State", [ "$type", Shape.Leaf; "key", Shape.Leaf ]
                ]
            "CellFormat",
            Map.ofList
                [
                    "Currency", [ "$type", Shape.Leaf; "code", Shape.Leaf; "decimals", Shape.Leaf; "locale", Shape.Union "LocaleSource" ]
                    "Custom", [ "$type", Shape.Leaf; "fn", Shape.Leaf ]
                    "DateTime", [ "$type", Shape.Leaf; "format", Shape.Leaf ]
                    "Duration", [ "$type", Shape.Leaf; "style", Shape.Leaf; "unit", Shape.Leaf ]
                    "None", [ "$type", Shape.Leaf ]
                    "Number", [ "$type", Shape.Leaf; "decimals", Shape.Leaf; "locale", Shape.Union "LocaleSource" ]
                    "Percent", [ "$type", Shape.Leaf; "decimals", Shape.Leaf; "locale", Shape.Union "LocaleSource" ]
                    "RelativeTime", [ "$type", Shape.Leaf; "unit", Shape.Leaf ]
                    "SignificantDigits", [ "$type", Shape.Leaf; "digits", Shape.Leaf ]
                ]
            "CellKindErased",
            Map.ofList
                [
                    "Button", [ "$type", Shape.Leaf; "label", Shape.Union "TextSource"; "onClick", Shape.Leaf ]
                    "ButtonGroup", [ "$type", Shape.Leaf; "buttons", Shape.List(Shape.Record "ButtonGroupItem") ]
                    "Checkbox", [ "$type", Shape.Leaf; "get", Shape.Leaf; "onToggle", Shape.Leaf ]
                    "Custom", [ "$type", Shape.Leaf; "fn", Shape.Leaf ]
                    "Date", [ "$type", Shape.Leaf ]
                    "Editable", [ "$type", Shape.Leaf; "onEdit", Shape.Leaf ]
                    "Link", [ "$type", Shape.Leaf; "hrefFn", Shape.Leaf; "labelFn", Shape.Leaf ]
                    "Numeric", [ "$type", Shape.Leaf ]
                    "Pill", [ "$type", Shape.Leaf; "labelFn", Shape.Leaf; "toneFn", Shape.Leaf ]
                    "Progress", [ "$type", Shape.Leaf; "fractionFn", Shape.Leaf; "labelFn", Shape.Leaf ]
                    "Text", [ "$type", Shape.Leaf ]
                    "TonedPill", [ "$type", Shape.Leaf; "default", Shape.Leaf; "field", Shape.Leaf; "map", Shape.Map(Shape.Leaf) ]
                ]
            "ChartAnnotation",
            Map.ofList
                [
                    "EventMarker", [ "$type", Shape.Leaf; "at", Shape.Union "ChartAnnotationX"; "label", Shape.Union "TextSource" ]
                    "RangeBand", [ "$type", Shape.Leaf; "label", Shape.Union "TextSource"; "range", Shape.Union "ChartAnnotationRange" ]
                    "ReferenceLine", [ "$type", Shape.Leaf; "label", Shape.Union "TextSource"; "value", Shape.Leaf ]
                    "ReferenceLineX", [ "$type", Shape.Leaf; "label", Shape.Union "TextSource"; "value", Shape.Leaf ]
                ]
            "ChartAnnotationRange",
            Map.ofList
                [
                    "ValueRange", [ "$type", Shape.Leaf; "from", Shape.Leaf; "to", Shape.Leaf ]
                    "XRange", [ "$type", Shape.Leaf; "from", Shape.Union "ChartAnnotationX"; "to", Shape.Union "ChartAnnotationX" ]
                ]
            "ChartAnnotationX",
            Map.ofList
                [
                    "Category", [ "$type", Shape.Leaf; "key", Shape.Leaf ]
                    "Date", [ "$type", Shape.Leaf; "iso", Shape.Leaf ]
                ]
            "ChartScale",
            Map.ofList
                [
                    "Diverging", [ "$type", Shape.Leaf; "max", Shape.Leaf; "midpoint", Shape.Leaf; "min", Shape.Leaf ]
                    "Sequential", [ "$type", Shape.Leaf; "max", Shape.Leaf; "min", Shape.Leaf ]
                ]
            "ColumnWidth",
            Map.ofList
                [
                    "Auto", [ "$type", Shape.Leaf ]
                    "Fixed", [ "$type", Shape.Leaf; "pixels", Shape.Leaf ]
                    "Flex", [ "$type", Shape.Leaf; "weight", Shape.Leaf ]
                ]
            "CurveCommand",
            Map.ofList
                [
                    "Close", [ "$type", Shape.Leaf ]
                    "CubicTo", [ "$type", Shape.Leaf; "control1", Shape.Record "DrawPoint"; "control2", Shape.Record "DrawPoint"; "to", Shape.Record "DrawPoint" ]
                    "LineTo", [ "$type", Shape.Leaf; "to", Shape.Record "DrawPoint" ]
                    "MoveTo", [ "$type", Shape.Leaf; "to", Shape.Record "DrawPoint" ]
                    "QuadraticTo", [ "$type", Shape.Leaf; "control", Shape.Record "DrawPoint"; "to", Shape.Record "DrawPoint" ]
                ]
            "FormFieldKind",
            Map.ofList
                [
                    "Checkbox", [ "$type", Shape.Leaf; "onChange", Shape.Leaf; "value", Shape.Leaf ]
                    "Choice", [ "$type", Shape.Leaf; "onChange", Shape.Leaf; "options", Shape.Union "Binding"; "value", Shape.Leaf ]
                    "Color", [ "$type", Shape.Leaf; "onChange", Shape.Leaf; "value", Shape.Leaf ]
                    "Combobox", [ "$type", Shape.Leaf; "allowFreeText", Shape.Leaf; "onChange", Shape.Leaf; "options", Shape.Union "Binding"; "value", Shape.Leaf ]
                    "DateTime", [ "$type", Shape.Leaf; "max", Shape.Leaf; "min", Shape.Leaf; "onChange", Shape.Leaf; "step", Shape.Leaf; "value", Shape.Leaf; "variant", Shape.Leaf ]
                    "DateTimeRange", [ "$type", Shape.Leaf; "max", Shape.Leaf; "min", Shape.Leaf; "onChange", Shape.Leaf; "step", Shape.Leaf; "value", Shape.Leaf; "variant", Shape.Leaf ]
                    "Number", [ "$type", Shape.Leaf; "onChange", Shape.Leaf; "value", Shape.Leaf ]
                    "Range", [ "$type", Shape.Leaf; "max", Shape.Leaf; "min", Shape.Leaf; "onChange", Shape.Leaf; "step", Shape.Leaf; "value", Shape.Leaf ]
                    "RangedNumber", [ "$type", Shape.Leaf; "max", Shape.Leaf; "min", Shape.Leaf; "onChange", Shape.Leaf; "step", Shape.Leaf; "value", Shape.Leaf ]
                    "Rating", [ "$type", Shape.Leaf; "allowHalf", Shape.Leaf; "max", Shape.Leaf; "onChange", Shape.Leaf; "value", Shape.Leaf ]
                    "SegmentedChoice", [ "$type", Shape.Leaf; "onChange", Shape.Leaf; "options", Shape.Union "Binding"; "orientation", Shape.Leaf; "value", Shape.Leaf ]
                    "Text", [ "$type", Shape.Leaf; "onChange", Shape.Leaf; "value", Shape.Leaf ]
                    "TextArea", [ "$type", Shape.Leaf; "onChange", Shape.Leaf; "rows", Shape.Leaf; "value", Shape.Leaf ]
                    "Toggle", [ "$type", Shape.Leaf; "onChange", Shape.Leaf; "value", Shape.Leaf ]
                    "Tokens", [ "$type", Shape.Leaf; "allowFreeText", Shape.Leaf; "onChange", Shape.Leaf; "suggestions", Shape.Union "Binding"; "value", Shape.Leaf ]
                ]
            "Format",
            Map.ofList
                [
                    "Currency", [ "$type", Shape.Leaf; "decimals", Shape.Leaf; "isoCode", Shape.Leaf ]
                    "DateTime", [ "$type", Shape.Leaf; "dateStyle", Shape.Leaf; "timeStyle", Shape.Leaf ]
                    "Duration", [ "$type", Shape.Leaf; "style", Shape.Leaf; "unit", Shape.Leaf ]
                    "Number", [ "$type", Shape.Leaf; "decimals", Shape.Leaf ]
                    "Percent", [ "$type", Shape.Leaf; "decimals", Shape.Leaf ]
                    "RelativeTime", [ "$type", Shape.Leaf; "unit", Shape.Leaf ]
                    "Since", [ "$type", Shape.Leaf; "unit", Shape.Leaf ]
                ]
            "FragmentArg",
            Map.ofList
                [
                    "Bool", [ "$type", Shape.Leaf; "value", Shape.Leaf ]
                    "Float", [ "$type", Shape.Leaf; "value", Shape.Leaf ]
                    "Int", [ "$type", Shape.Leaf; "value", Shape.Leaf ]
                    "SlotArg", [ "$type", Shape.Leaf; "tree", Shape.Node ]
                    "Str", [ "$type", Shape.Leaf; "value", Shape.Leaf ]
                ]
            "HoleDecl",
            Map.ofList
                [
                    "Repeat", [ "$type", Shape.Leaf; "countSpace", Shape.Union "HoleValueSpace"; "name", Shape.Leaf ]
                    "Slot", [ "$type", Shape.Leaf; "kindConstraint", Shape.Leaf; "name", Shape.Leaf ]
                    "Value", [ "$type", Shape.Leaf; "default", Shape.Union "Scalar"; "name", Shape.Leaf; "space", Shape.Union "HoleValueSpace" ]
                ]
            "HoleValueSpace",
            Map.ofList
                [
                    "AnyString", [ "$type", Shape.Leaf ]
                    "Enum", [ "$type", Shape.Leaf; "choices", Shape.List(Shape.Leaf) ]
                    "FloatRange", [ "$type", Shape.Leaf; "max", Shape.Leaf; "min", Shape.Leaf ]
                    "IntRange", [ "$type", Shape.Leaf; "max", Shape.Leaf; "min", Shape.Leaf ]
                    "StringLen", [ "$type", Shape.Leaf; "maxLen", Shape.Leaf; "minLen", Shape.Leaf ]
                ]
            "LocalFlushTrigger",
            Map.ofList
                [
                    "OnBlur", [ "$type", Shape.Leaf ]
                    "OnCommitAction", [ "$type", Shape.Leaf ]
                    "OnDebounce", [ "$type", Shape.Leaf; "milliseconds", Shape.Leaf ]
                    "OnSubmit", [ "$type", Shape.Leaf ]
                ]
            "LocaleSource",
            Map.ofList
                [
                    "Ambient", [ "$type", Shape.Leaf ]
                    "Explicit", [ "$type", Shape.Leaf; "tag", Shape.Leaf ]
                ]
            "MediaKind",
            Map.ofList
                [
                    "Audio", [ "$type", Shape.Leaf ]
                    "Video", [ "$type", Shape.Leaf; "autoplay", Shape.Leaf; "poster", Shape.Union "Binding" ]
                ]
            "Scalar",
            Map.ofList
                [
                    "Bool", [ "$type", Shape.Leaf; "value", Shape.Leaf ]
                    "Float", [ "$type", Shape.Leaf; "value", Shape.Leaf ]
                    "Int", [ "$type", Shape.Leaf; "value", Shape.Leaf ]
                    "Str", [ "$type", Shape.Leaf; "value", Shape.Leaf ]
                ]
            "Shape",
            Map.ofList
                [
                    "Circle", [ "$type", Shape.Leaf; "cx", Shape.Leaf; "cy", Shape.Leaf; "r", Shape.Leaf; "style", Shape.Record "DrawStyle" ]
                    "Curve", [ "$type", Shape.Leaf; "commands", Shape.List(Shape.Union "CurveCommand"); "style", Shape.Record "DrawStyle" ]
                    "Ellipse", [ "$type", Shape.Leaf; "cx", Shape.Leaf; "cy", Shape.Leaf; "rx", Shape.Leaf; "ry", Shape.Leaf; "style", Shape.Record "DrawStyle" ]
                    "Group", [ "$type", Shape.Leaf; "children", Shape.List(Shape.Union "Shape"); "style", Shape.Record "DrawStyle" ]
                    "Label", [ "$type", Shape.Leaf; "style", Shape.Record "DrawStyle"; "text", Shape.Union "TextSource"; "x", Shape.Leaf; "y", Shape.Leaf ]
                    "Line", [ "$type", Shape.Leaf; "style", Shape.Record "DrawStyle"; "x1", Shape.Leaf; "x2", Shape.Leaf; "y1", Shape.Leaf; "y2", Shape.Leaf ]
                    "Polygon", [ "$type", Shape.Leaf; "points", Shape.List(Shape.Record "DrawPoint"); "style", Shape.Record "DrawStyle" ]
                    "Polyline", [ "$type", Shape.Leaf; "points", Shape.List(Shape.Record "DrawPoint"); "style", Shape.Record "DrawStyle" ]
                    "Rectangle", [ "$type", Shape.Leaf; "cornerRadius", Shape.Leaf; "height", Shape.Leaf; "style", Shape.Record "DrawStyle"; "width", Shape.Leaf; "x", Shape.Leaf; "y", Shape.Leaf ]
                ]
            "TextSource",
            Map.ofList
                [
                    "Bound", [ "$type", Shape.Leaf; "binding", Shape.Union "Binding" ]
                    "I18n", [ "$type", Shape.Leaf; "args", Shape.Map(Shape.Leaf); "key", Shape.Leaf ]
                    "Literal", [ "$type", Shape.Leaf; "text", Shape.Leaf ]
                ]
        ]
