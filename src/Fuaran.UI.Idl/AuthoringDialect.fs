module Fuaran.UI.AuthoringDialect

// ---------------------------------------------------------------------------
// Phase 2241 — the DECLARED half of the in-repo authoring surfaces, beside the
// vocabulary they are generated from.
//
// The VB XML-literal analyzer's vocabulary (`Kinds`, `Structural`, `Attributes`
// in `src/Fuaran.UI.Analyzers/VisualBasic/Vocabulary.Generated.cs`) and the C#
// veneer's enum translation seam (`src/Fuaran.UI.CSharp/Facade/EnumMap.Generated.cs`)
// are EMITTED by `AuthoringMirrors` from `idl.json` plus this file, and checked by
// the same regeneration test that checks `Generated.fs`
// (`FUARAN_REGEN=1 dotnet run --project src/Fuaran.UI.Idl.Tests`).
//
// What is DERIVED needs no declaration. A wire kind's attribute row is its
// attribute-eligible fields spelled in kebab-case, plus `id`; a field whose type
// is structured has no attribute spelling by construction. What this file holds
// is only where the dialect DIVERGES from that derivation, each with its reason:
// the element conveniences the wire has no kind for, the structural child
// elements and the IDL slot each spells, the renames, flattenings, sugar
// families and gaps. These tables used to live in two test pins
// (`AuthoringSurfacePin`, `StructuralElementPin`) beside a hand-kept mirror they
// guarded; they are generator input now, and every entry is ORPHAN-REFUSING —
// a declaration that has stopped being true fails the generation rather than
// outliving its subject, because the whole value of the enumeration is that it
// is complete.
// ---------------------------------------------------------------------------

/// An element the dialect spells that is not a wire kind: an element
/// convenience that lowers onto a kind, or a structural child element.
type DialectElement =
    {
        Name: string
        /// What the element is — and, for a structural child, the IDL slot it
        /// spells as `Kind.field` or `Record.field`, resolved at generation.
        Anchor: string option
        Note: string
        /// Its attribute row; `[]` with `HasRow = false` leaves it allow-any.
        Attributes: string list
        HasRow: bool
    }

let private convenience name note attrs =
    { Name = name
      Anchor = None
      Note = note
      Attributes = attrs
      HasRow = true }

let private child name anchor note attrs =
    { Name = name
      Anchor = Some anchor
      Note = note
      Attributes = attrs
      HasRow = not (List.isEmpty attrs) }

/// Element conveniences: spelled as elements, lowered onto a wire kind by the
/// translator. They are recognised as KINDS by the analyzer.
let conveniences: DialectElement list =
    [ convenience "Dashboard" "emits Box with role Dashboard" [ "id" ]
      convenience "Stack" "emits Box with a Flex layout" [ "id"; "orientation"; "wrap" ]
      convenience "Grid" "emits Box with a Grid layout" [ "id"; "cols" ]
      convenience "Card" "emits Box with role Card" [ "id"; "heading" ]
      convenience "Divider" "emits Box with role Separator" [ "id"; "orientation"; "label" ]
      // Phase 801 / 873 — the static table's declared sort intent.
      // `default-sort-column` indexes the <Header> children; a direction with no
      // column names no order, so the pair is read as one.
      convenience
          "Table"
          "emits the static-rows DataGrid the retired Table kind expressed"
          [ "id"; "sortable"; "default-sort-column"; "default-sort-direction" ]
      convenience "MultiSelect" "emits Select with multiple set" [ "id"; "label"; "values" ] ]

/// Structural child elements — children of a kind, not kinds themselves. Each
/// names the IDL slot it spells. Membership is not cosmetic: the analyzer's
/// `IsKnownElement` is `Kinds ∪ Structural`, and it reports FUARAN150 and stops
/// on anything outside it, so an omission is a false positive on valid
/// authoring AND silently skips that element's attribute check.
let structuralChildren: DialectElement list =
    [
      // Singular-of-the-field — the ordinary case.
      child "Item" "List.items" "one item of the list" []
      child
          "TreeItem"
          "Tree.items"
          "one TreeItem, and the only structural element that nests INSIDE ITSELF (Phase 1120). Not an overloaded <Item>: the attribute table is keyed by element name, so an `id`/`label`/`icon` row on <Item> would make `<Item id=\"x\">` inside a <List> analyzer-clean while the translator ignored it"
          [ "id"; "label"; "icon" ]
      // Phase 864 / 873 — the `rule-*` family carries the field's declared
      // constraint. Phases 1113 / 1130 — `allowFreeText`, `max` and `allowHalf`
      // are spelled on both <Field> and <Filter>: a chip carries the same
      // control since the 0.2.0 unification. Phase 1121 — the Tokens field
      // needs no attribute of its own (it reuses `allowFreeText`, `initial` and
      // <Option> children).
      child
          "Field"
          "Form.fields"
          "one FormField"
          [ "kind"
            "id"
            "label"
            "required"
            "initial"
            "help"
            "selected"
            "rows"
            "allowFreeText"
            "max"
            "allowHalf"
            "rule-format"
            "rule-pattern"
            "rule-min-length"
            "rule-max-length"
            "rule-compare-field"
            "rule-compare-op"
            "rule-message" ]
      child
          "Filter"
          "Filters.items"
          "one FilterSpec; the element is named for the domain, the field for the shape"
          [ "kind"; "name"; "label"; "allowFreeText"; "max"; "allowHalf" ]
      // Phase 750 — `tone-field` / `default-tone` accompany <Tone> children.
      // Phases 861 / 863 / 873 — `sortable` / `editable` on a column narrow the
      // grid's behaviour and never widen it; absent means inherit.
      child
          "Column"
          "DataGrid.columns"
          "one ColumnErased"
          [ "type"
            "label"
            "field"
            "tone-field"
            "default-tone"
            "sortable"
            "editable" ]
      child "Case" "Switch.cases" "one SwitchCase" [ "match" ]

      // Renames — the element name and the wire field name differ.
      child "Source" "Image.srcSet" "rename — one SrcSetEntry of the srcSet list (Phase 1080)" []
      // Phase 1110 — `srclang` is spelled as the HTML attribute is, not as the
      // wire's camelCase `srcLang`.
      child
          "Track"
          "Media.tracks"
          "one TrackEntry of the tracks list (Phase 1110)"
          [ "kind"; "src"; "srclang"; "label"; "default" ]
      child
          "Option"
          "Select.source"
          "rename — one SelectOption; the literal children build the Binding the `source` field carries"
          [ "value"; "label" ]
      child
          "Marker"
          "Map.source"
          "rename — one MapMarker; the literal children build the Binding the `source` field carries"
          [ "lat"; "lng"; "label" ]
      // The three ChartAnnotation arms (Phases 1490-1492), each named for its
      // union arm. §4l gives an annotation an address and a label and nothing
      // else, so there is no colour, opacity, dash, side or offset attribute.
      child
          "ReferenceLine"
          "Chart.annotations"
          "the ReferenceLine arm; `value` is in the value axis's own units"
          [ "value"; "label" ]
      child
          "EventMarker"
          "Chart.annotations"
          "the EventMarker arm; its x address is two attributes, exactly one of which is given"
          [ "category"; "date"; "label" ]
      child
          "RangeBand"
          "Chart.annotations"
          "the RangeBand arm; its interval is one complete attribute pair out of three"
          [ "fromValue"
            "toValue"
            "fromCategory"
            "toCategory"
            "fromDate"
            "toDate"
            "label" ]
      child "Prop" "Custom.props" "rename — one entry of the props map, authored as name/value" [ "name"; "value" ]

      // Node-valued slots — the child element names the slot.
      child "Child" "ErrorBoundary.child" "the guarded subtree" []
      child "Fallback" "ErrorBoundary.fallback" "the subtree shown when the guard trips" []
      child "Body" "FragmentDecl.body" "the declared fragment's subtree" []
      child "Default" "Switch.default" "the subtree taken when no case matches" []

      // Slots inside a RECORD reached from a kind field.
      child "Header" "StaticRows.headers" "one header cell of the <Table> convenience's StaticRows record" []
      child "Row" "StaticRows.rows" "one row of the <Table> convenience's StaticRows record" []
      child
          "Cell"
          "StaticRows.rows"
          "the inner element of the rows list — a row IS a list of cells, so it shares <Row>'s anchor"
          []
      child
          "Tone"
          "ColumnErased.kind"
          "one entry of the `map` on the TonedPill arm of a column's kind union (Phase 750)"
          [ "value"; "tone" ] ]

/// Wire kinds the analyzer leaves allow-any (no attribute row), with why.
let noAttributeRow: (string * string) list =
    [ "Box",
      "authored only through its four conveniences (<Dashboard> / <Stack> / <Grid> / <Card>), each of which has its own row" ]

/// Attributes every element admits, because they spell a NODE-ENVELOPE trait
/// rather than a field of any one kind: `tooltip` (Phase 1112), `direction`
/// (Phase 1472), `visible` (Phase 1535). Each must name an attribute-eligible
/// envelope field.
let universal: string list = [ "tooltip"; "direction"; "visible" ]

/// Kinds whose row carries the shared `format-*` sugar family — four
/// attributes authoring the one CellFormat-valued `format` field.
let cellFormatSugar: string list = [ "Metric"; "LabelValueRow" ]

/// The `format-*` family itself.
let cellFormatFamily: string list =
    [ "format-currency"; "format-number"; "format-percent"; "format-date-time" ]

/// Attribute-eligible wire fields the dialect does NOT spell, as
/// `Kind.attribute`. A RENAME is authorable under another name; a GAP is an
/// authoring capability the VB tier does not have.
let unspelledFields: (string * string) list =
    [
      // Renames — spelled, under a different attribute name.
      "Map.centre-latitude", "rename — spelled `centre-lat`"
      "Map.centre-longitude", "rename — spelled `centre-lng`"
      "Switch.on",
      "rename — spelled `state-key`; the generated projection merges the wire's on/stateKey pair into one slot"

      // Gaps — no VB spelling at all.
      "Button.disabled", "GAP — no VB spelling"
      "Button.icon", "GAP — no VB spelling"
      "Form.disabled", "GAP — no VB spelling"
      "Select.disabled", "GAP — no VB spelling"
      "Select.multiple", "GAP — no VB spelling; <MultiSelect> is the multi-select convenience"
      "FileUpload.disabled", "GAP — no VB spelling"
      "Link.protection", "GAP — no VB spelling"
      "Metric.icon", "GAP — no VB spelling"
      "Metric.subtext", "GAP — no VB spelling"
      "Metric.emphasis", "GAP — no VB spelling"
      "Metric.weight", "GAP — no VB spelling"
      "Tabs.active-tag", "GAP — no VB spelling" ]

/// Attributes a wire kind's row carries that are not attribute-eligible fields
/// of it, as `Kind.attribute`: a record flattened into its parts, a sugar
/// family, a narrowed spelling of a structured field, or a rename.
let attributeOnly: (string * string) list =
    [
      // Record flattenings.
      "Mount.two-way", "flattening — with `message-shape`, authors the GuestChannel record"
      "Mount.message-shape", "flattening — with `two-way`, authors the GuestChannel record"
      "DataGrid.default-sort-column", "flattening — with `default-sort-direction`, authors the DefaultSort record"
      "DataGrid.default-sort-direction", "flattening — with `default-sort-column`, authors the DefaultSort record"
      "Drawing.min-x", "flattening — one part of the ViewBox record"
      "Drawing.min-y", "flattening — one part of the ViewBox record"
      "Drawing.width", "flattening — one part of the ViewBox record"
      "Drawing.height", "flattening — one part of the ViewBox record"
      "Media.kind", "flattening — selects the MediaKind variant (video | audio)"
      "Media.autoplay", "flattening — a slot of MediaKind.Video, read only on the video branch"
      "Media.poster", "flattening — a slot of MediaKind.Video, read only on the video branch"

      // Sugar families.
      "Chart.value-format-currency", "sugar — one arm of the Format union on `valueFormat`"
      "Chart.value-format-number", "sugar — one arm of the Format union on `valueFormat`"
      "Chart.value-format-percent", "sugar — one arm of the Format union on `valueFormat`"

      // Narrowed spellings.
      "Chart.source", "narrowed — a query name, widened to Binding on translation"
      "DataGrid.source", "narrowed — a query name, widened to Binding on translation"
      "Sparkline.source", "narrowed — a query name, widened to Binding on translation"
      "Chart.y-fields", "narrowed — a comma-separated string authoring a list<str>"
      "Mount.capabilities", "narrowed — a comma-separated string authoring a list<str>"
      "Custom.exposed-node-ids", "narrowed — a comma-separated string authoring a list<str>"
      "FileUpload.accept", "narrowed — a comma-separated string authoring a list<str>"
      "Embed.permissions", "narrowed — a pipe-separated string authoring a list<EmbedPermission> (Phase 1111)"

      // Renames — the attribute side of an entry in `unspelledFields`.
      "Map.centre-lat", "rename — spells the wire's `centreLatitude`"
      "Map.centre-lng", "rename — spells the wire's `centreLongitude`" ]

/// Structured wire fields with NO child-element spelling, as `Kind.field`.
/// Flattenings, narrowings and sugar are authorable as attributes (their
/// attribute side is in `attributeOnly`); a GAP is not authorable at all.
let notChildAuthorable: (string * string) list =
    [ "Box.layout", "flattening — the convenience elements' `orientation` / `wrap` / `cols` attributes"
      "Chart.source", "narrowed — the `source` attribute carries a query name, widened to a Binding on translation"
      "Chart.yFields", "narrowed — `y-fields`, a comma-separated string"
      "Chart.valueFormat", "sugar — the `value-format-*` attribute family"
      "Custom.exposedNodeIds", "narrowed — `exposed-node-ids`, a comma-separated string"
      "DataGrid.defaultSort", "flattening — `default-sort-column` + `default-sort-direction`"
      "DataGrid.source", "narrowed — the `source` attribute carries a query name"
      "DataGrid.staticRows",
      "spelled INDIRECTLY — the <Table> convenience builds this record from <Header> / <Row> / <Cell>"
      "Drawing.viewBox", "flattening — `min-x` / `min-y` / `width` / `height`"
      "FileUpload.accept", "narrowed — `accept`, a comma-separated string"
      "LabelValueRow.format", "sugar — the shared `format-*` attribute family"
      "Media.kind", "flattening — `kind` selects the variant; `autoplay` / `poster` are its video-branch slots"
      "Metric.format", "sugar — the shared `format-*` attribute family"
      "Metric.trendFormat", "sugar — the shared `format-*` attribute family"
      "Mount.capabilities", "narrowed — `capabilities`, a comma-separated string"
      "Embed.permissions", "narrowed — `permissions`, a pipe-separated string of relaxation names (Phase 1111)"
      "Mount.channel", "flattening — `two-way` + `message-shape`"
      "Select.values", "narrowed — the <MultiSelect> convenience's `values` attribute"
      "Sparkline.source", "narrowed — the `source` attribute carries a query name"

      // Gaps — no VB spelling at all, attribute or element.
      "Button.onClick", "GAP — the VB tier spells no Action vocabulary, in either shape"
      "Form.onSubmit", "GAP — the VB tier spells no Action vocabulary, in either shape"
      "Modal.onDismiss", "GAP — the VB tier spells no Action vocabulary, in either shape"
      "CodeBlock.highlightLines", "GAP — no VB spelling"
      "Custom.contentHash", "GAP — no VB spelling"
      "Drawing.shapes", "GAP — no VB spelling; the XML dialect spells only a Drawing's viewBox and titling"
      "Drawing.style", "GAP — no VB spelling"
      "FragmentDecl.holes", "GAP — no VB spelling"
      "FragmentDecl.effect", "GAP — no VB spelling"
      "FragmentRef.args", "GAP — no VB spelling"
      "Mount.inputs", "GAP — no VB spelling"
      // Phase 2472 — the further comparisons; the VB <Metric> spells only its
      // primary trend, beside the icon / subtext / weight / emphasis gaps.
      "Metric.comparisons", "GAP — no VB spelling"
      // Labels are child-inferred. If <Tabs> gains header/tag authoring, port
      // FUARAN047/048 (header/tag-vs-children count) into the VB analyzer in
      // the same change.
      "Tabs.tabHeaders", "GAP — labels are child-inferred"
      "Tabs.tabTags", "GAP — the tag half of the same gap"
      // Phase 2517 — the operability affordance is authored in F# / on the wire;
      // the VB tier spells no agent-facing vocabulary yet.
      "Button.affordance", "GAP — no VB spelling"
      "Select.affordance", "GAP — no VB spelling" ]

/// One C# veneer enum and the IDL enum it mirrors. The C# DECLARATION stays
/// hand-authored, because it carries per-member public documentation the IDL
/// has no slot for; its case list is the IDL's host case list (pinned by the
/// conformance suite's `EnumTokenPin`). What is generated is the `ToFs`
/// translation seam, one arm per IDL case, so a case added to the IDL fails the
/// C# compile at the declaration it is missing from instead of mapping silently.
/// An out-of-range value (a cast integer, a combined value) has no arm and
/// THROWS: Phase 1930 removed the per-enum fallback case, which made
/// `(EmbedPermission)99` author `AllowScripts`.
type CSharpEnumMirror = { CSharp: string; Idl: string }

let private mirror cs idl = { CSharp = cs; Idl = idl }

/// The C# veneer's enum mirrors.
let csharpEnums: CSharpEnumMirror list =
    [ mirror "Tone" "ToneVariant"
      mirror "Weight" "StyleWeight"
      mirror "Emphasis" "Emphasis"
      mirror "ButtonVariant" "ButtonVariant"
      mirror "BadgeVariant" "BadgeVariant"
      mirror "TextDirection" "TextDirection"
      mirror "Orientation" "Orientation"
      mirror "HeadingVariant" "HeadingVariant"
      mirror "ImageVariant" "ImageVariant"
      mirror "ImageFit" "ImageFit"
      mirror "ImageAspect" "ImageAspect"
      mirror "ImageLoading" "ImageLoading"
      mirror "TrackKind" "TrackKind"
      mirror "EmbedPermission" "EmbedPermission"
      mirror "ScrollOrientation" "ScrollOrientation"
      mirror "Modality" "ModalityKind"
      mirror "CaptureSource" "CaptureSource"
      mirror "NavigateTarget" "NavigateTarget"
      mirror "MathDisplay" "MathDisplay"
      mirror "IconSize" "IconSize"
      mirror "TrendPolarity" "TrendPolarity"
      mirror "SortDirection" "SortDirection"
      mirror "TextFormat" "TextFormat"
      mirror "CompareOp" "CompareOp"
      mirror "DateStyle" "DateStyle"
      mirror "TimeStyle" "TimeStyle"
      mirror "RelativeTimeUnit" "RelativeTimeUnit"
      mirror "ChartKind" "ChartKind"
      mirror "ChartLegendPosition" "ChartLegendPosition"
      mirror "ChartDataLabels" "ChartDataLabels"
      mirror "ChartXScale" "ChartXScale"
      mirror "TimeGrain" "TimeGrain"
      mirror "DurationUnit" "DurationUnit"
      mirror "DurationStyle" "DurationStyle"
      mirror "FileEncoding" "FileReadEncoding" ]
