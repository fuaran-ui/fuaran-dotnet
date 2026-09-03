module Fuaran.UI.VocabularySupport

// ---------------------------------------------------------------------------
// Phase 945 (fuaran-ui workspace) — the declared-support record for the UI
// vocabulary's generated layer.
//
// Everything here USED to be a hand edit to the generated artefact — the
// "hand-added ahead of the IDL backfill" regions that Phases 812/818/819/821
// left in `Fuaran-UI/fuaran-dotnet/src/Fuaran.UI/Generated.fs` while the IDL
// caught up. The catch-up left the two copies diverged in both directions, and
// the tier sync (WIRE_FORMAT §11 step 1) regressed the tier whenever it ran.
// Phase 945 moved the content HERE, beside the IDL, as data the generator
// emits: the docs land on the generated declarations, the splices join the
// generated recursion groups verbatim, and the `Switch` host projection keeps
// the tier's merged `On: Binding<string>` API while the IDL keeps describing
// the wire's two optional keys. The emission is reproducible again, and the
// sync is a byte-copy again.
//
// The one semantic seam to understand before editing: `TransformSource` is a
// DISCRIMINATED-BY-INSPECTION union (a binding-shaped `$type` versus a plain
// columnar shape) — not a `$type`-tagged union the IDL could model — so its
// type and codecs are verbatim splices, and the Transform `source` slot is a
// `THosted` pointing at them by name.
// ---------------------------------------------------------------------------

open Fuaran.Core.Idl

/// The `Switch` host projection: the wire has TWO optional keys (`on`, the
/// compact `stateKey`), the F# API has ONE required `On: Binding<string>` —
/// Phase 768's merge, kept while the IDL keeps describing the wire.
let private switchProjection: Gen.KindProjection =
    { SpecDecl =
        """SwitchSpec<'Msg> =
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
    }"""
      Encoder =
        """and private encSwitchSpec<'Msg> (s: SwitchSpec<'Msg>) : JVal =
    Canon.typed "Switch" ([ Some("cases", JArr(List.map encSwitchCase s.Cases)); Some("default", encNode s.Default); (match s.On with | Binding.State (key, None) -> Some("stateKey", JStr key) | on -> Some("on", (encBinding JStr) on)); (s.AutoAdvanceMs |> Option.map (fun v -> "autoAdvanceMs", JInt v)) ] |> List.choose id)"""
      Decoder =
        """and private decSwitchSpec (j: JVal) : Result<SwitchSpec<obj>, string> =
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
    Ok { Cases = cases; Default = ``default``; On = on; AutoAdvanceMs = autoAdvanceMs })))))"""
      // A projected kind supplies its OWN `mk`, so the node envelope is spelled
      // here by hand rather than derived from `Idl.NodeFields` the way the
      // forty-one generated constructors' envelope is. A new envelope field
      // therefore has to be added here too — Fuaran-UI Phase 1112's `tooltip` is
      // the first one to land since this projection seam did, and it arrived as
      // `FS0764` on this literal alone. The coupling is left as it is
      // deliberately: the compiler names the one site, in the same build that
      // adds the field, which is a stronger guarantee than a convention nobody
      // re-reads. What is added is this note, so the next envelope field is
      // expected here rather than discovered here.
      Mk =
        Some
            """let mkSwitch (id: string) (cases: SwitchCase<'Msg> list) (``default``: Node<'Msg>) (stateKey: string) : Node<'Msg> =
    { Id = id; Kind = NodeKind.Switch { Cases = cases; Default = ``default``; On = Binding.State(stateKey, None); AutoAdvanceMs = None }; Accessibility = None; ExtraAttributes = None; Motion = None; State = None; Style = None; Tooltip = None }""" }

/// The declared support for `Gen.fsharpModuleWith` — see the module doc above.
let support: Gen.GenSupport =
    { Gen.GenSupport.Empty with
        Docs =
            Map.ofList
                [ "case:Action.SetState",
                  [ "// Phase 818 — `valueFrom` (a Binding evaluated at dispatch time inside the"
                    "// existing gate) is a SIBLING of the literal `value`; decode enforces"
                    "// value XOR valueFrom. `value` became an option in the same change so the"
                    "// valueFrom-only wire shape is representable without a placeholder." ]
                  "case:Binding.Transform",
                  [ "// Phase 818 — the source slot widened from `Fuaran.Core.DataSource` to the"
                    "// host `TransformSource` DU so a binding-shaped wire source (State /"
                    "// Selection / Query) is PRESERVED for live re-evaluation instead of being"
                    "// snapshotted at decode (the Phase-815 leniency's semantics upgrade)." ]
                  "case:CellFormat.Duration",
                  [ "/// Phase 819 — trendable duration cells: the raw float counts `unit`s,"
                    "/// rendered per `style`." ]
                  "case:CellFormat.RelativeTime",
                  [ "/// Phase 819 — cell-vocabulary parity with `Format.RelativeTime`: the"
                    "/// raw float is a signed count of `unit`." ]
                  "case:ChartDataLabels.Ends",
                  [ "/// Label the ENDS only: every bar's cap (a stacked bar's TOTAL at the stack"
                    "/// cap, never its interior segments) and the last point of every line or"
                    "/// area edge." ]
                  "case:ChartDataLabels.Off",
                  [ "/// No data labels (the shipped default, and what an absent field means)." ]
                  "case:ChartLegendPosition.Bottom", [ "/// The same horizontal row, mirrored below the x-axis title." ]
                  "case:ChartLegendPosition.None", [ "/// No legend box at all." ]
                  "case:ChartLegendPosition.Right",
                  [ "/// A vertical column on the right — one row per series, the plot shrinking"
                    "/// by the column's width. The shipped default." ]
                  "case:ChartLegendPosition.Top",
                  [ "/// A horizontal row in the top margin, under the title (the pre-880 shape)." ]
                  "case:ChartXScale.Category",
                  [ "/// Discrete categories, one band per row, in row order (the shipped"
                    "/// default, and what an absent field means)." ]
                  "case:ChartXScale.Temporal",
                  [ "/// Dates: the x column carries canonical ISO-8601 dates (`YYYY-MM-DD`, or a"
                    "/// timestamp whose time-of-day is discarded) and the axis is CONTINUOUS —"
                    "/// points sit at their date, ticks land on calendar boundaries, and the"
                    "/// tick labels adapt to the data's granularity." ]
                  "case:Format.Duration",
                  [ "/// Phase 819 — locale-independent duration formatting: the numeric"
                    "/// source counts `unit`s, rendered per `style`." ]
                  "dec:DurationUnit", [ "// Phase 819 — Duration format enums." ]
                  "dec:Icon", [ "// Phase 821 — Icon display kind." ]
                  "dec:IconSize", [ "// Phase 821 — Icon size class." ]
                  "decarm:Binding.Now",
                  [ "// Identity accessor (the Phase 427 Selection fix replayed): the"
                    "// host-furnished instant is already the wire-shaped string, so a"
                    "// decoded reader receives it as-is; a value-discarding placeholder"
                    "// would make every decoded `Now` resolve to nothing." ]
                  "decarm:Binding.Transform",
                  [ "// Phase 818 — a binding-shaped source (State / Selection / Query"
                    "// `$type`) is preserved as `TransformSource.Live`; the initial"
                    "// snapshot derives from the binding's carried default data via the"
                    "// host-prelude helpers (a State source must carry data — the"
                    "// Phase-815 posture; Selection/Query fall back to the empty"
                    "// table). Anything else decodes through Core's columnar codec as"
                    "// before, byte-identical." ]
                  "enc:DurationUnit", [ "// Phase 819 — Duration format enums." ]
                  "enc:Icon",
                  [ "// Phase 821 — Icon display kind."
                    "// `size` omitted-when-`Medium`, `tone` omitted-when-`Default`, `label`"
                    "// omitted-when-`None` (decorative)." ]
                  "enc:IconSize", [ "// Phase 821 — Icon size class." ]
                  "encarm:Action.SetState",
                  [ "// Phase 818 — `value` / `valueFrom` are XOR siblings; each is emitted only"
                    "// when present (Canon sorts keys, so the field order stays alphabetical)." ]
                  "field:BoxSpec.BreakBefore",
                  [ "// Phase 1473 — the box starts at the top of a fresh page when the"
                    "// rendering is paged. `break-before: page` on a paged medium and"
                    "// nothing at all on a continuous one, so a screen rendering is"
                    "// byte-for-byte the rendering it always was."
                    "//"
                    "// There is deliberately NO break-AFTER counterpart: a break after this"
                    "// box is a break before the next one, so a second spelling would buy"
                    "// nothing and would be exactly the near-synonym pressure the vocabulary"
                    "// charter's §3.2 confusion review exists to prevent."
                    "//"
                    "// Omitted on the wire at `false`." ]
                  "field:BoxSpec.KeepTogether",
                  [ "// Phase 1473 — the box and everything under it stay on ONE page when the"
                    "// rendering is paged: `break-inside: avoid`, and nothing at all on a"
                    "// continuous medium."
                    "//"
                    "// The declaration is IRREDUCIBLE in the charter's §1.2 sense and this is"
                    "// the clearest instance of it: a host laying out pages sees boxes, and"
                    "// cannot infer that the totals block is ONE THING that reads wrong when"
                    "// halved. Only the tree knows its own subtrees, and no rendering carries"
                    "// that fact back. It is the `sortStateKey` shape — a behaviour the host"
                    "// performs, keyed by something only the document can name."
                    "//"
                    "// It names nothing about the MEDIUM: no page size, no margin, no sheet"
                    "// number, no running header or footer. Those are the host's, and the"
                    "// ratified charter row keeps them out of the language."
                    "//"
                    "// Omitted on the wire at `false`." ]
                  "field:ChartSpec.DataLabels",
                  [ "// Phase 881 — whether the values are written onto the picture. Semantic"
                    "// in the same way (D8): whether a reader is meant to read the NUMBERS or"
                    "// the shape is the author's meaning; the type size, the offsets and the"
                    "// fit rule that decide whether a given label actually draws are the"
                    "// host's, in `ChartStyle`."
                    "//"
                    "// Absent means `Off`, and `Off` is also the shipped default — the one"
                    "// place this field differs from `LegendPosition`, deliberately: a legend"
                    "// is chrome an author is opting OUT of, where a data label is ink an"
                    "// author is opting IN to. So an absent field is byte-identical to the"
                    "// pre-881 wire and to the pre-881 picture." ]
                  "field:ChartSpec.LegendPosition",
                  [ "// Phase 880 — WHERE the legend sits, and whether it sits anywhere at all."
                    "// Semantic for the same reason the titles above are (D8): the edge an"
                    "// author wants the legend on is their meaning; the column widths and"
                    "// pitches that realise it are the host's, in `ChartStyle`."
                    "//"
                    "// Absent means \"the style's default\" (`ChartStyle.LegendPosition`, which"
                    "// ships as `Right`) — NOT \"no legend\"; suppression is the explicit"
                    "// `ChartLegendPosition.None`. So absence stays the ordinary shape and is"
                    "// omitted on the wire, and an author who wants no legend says so." ]
                  "field:ChartSpec.Subtitle",
                  [ "// The muted line under the visible title — the natural home for a units"
                    "// statement (\"Revenue by quarter / £m\"). An explicit subtitle SUPPRESSES"
                    "// the lowering's own display-unit slot (Phase 876): the author has said"
                    "// it, so the machine does not repeat it." ]
                  "field:ChartSpec.ValueFormat",
                  [ "// Phase 876 — the VALUE axis's number format, reusing the existing"
                    "// `Format` vocabulary (the Phase 819 family) rather than minting a"
                    "// parallel formatting DU. It is a SEMANTIC declaration (\"these numbers"
                    "// are pounds / a ratio / two-decimal quantities\"), which is why it is a"
                    "// wire field where `ChartStyle` is not (D8): appearance is the host's,"
                    "// meaning is the author's. Absent means \"no declared meaning\" — the"
                    "// lowering still applies its canonical default rendering (thousands"
                    "// separators + step-derived decimals), which is a LOWERING behaviour,"
                    "// not wire state. Omitted on the wire when absent." ]
                  "field:ChartSpec.XScale",
                  [ "// Phase 882 — what the x column MEANS: discrete categories, or dates on a"
                    "// continuous temporal scale. Semantic in the same way (D8): whether a"
                    "// column is a set of categories or a run of dates is a fact about the"
                    "// data; the tick ladder, the label format and the margins that realise it"
                    "// are the host's, in `ChartStyle`."
                    "//"
                    "// Absent means `Category`, which is also the shipped default, so an"
                    "// absent field is byte-identical to the pre-882 wire AND to the pre-882"
                    "// picture. `Temporal` is a DECLARATION the pre-emit validator grounds"
                    "// against the column type (FUARAN097) — never an inference from the data,"
                    "// which would make the same tree draw differently depending on where its"
                    "// rows came from." ]
                  "field:ChartSpec.XTitle",
                  [ "// Phase 878 — the axis NAMES and the subtitle. Semantic wire fields for"
                    "// the same reason `Title` is one and `ChartStyle` is not (D8): what an"
                    "// axis is CALLED is the author's meaning; where and how it is drawn is"
                    "// the host's appearance."
                    "//"
                    "// All three are DEFAULT-ON in the sense that matters: absent `XTitle` /"
                    "// `YTitle` fall back to the capitalised field name, so an axis is never"
                    "// nameless. Absent is therefore the ordinary shape, not an opt-out —"
                    "// omitted on the wire, and identical to what the author would have"
                    "// written by hand." ]
                  "field:ColumnErased.Editable",
                  [ "// Phase 863 — per-column EDITABILITY narrowing, the same rule on the"
                    "// write side. Absent = inherit the grid-level `editable`. `false` makes"
                    "// this column read-only under a grid-level `true` — the declaration"
                    "// \"read-only implied by omission\" could not express. `true` is the"
                    "// inherited default made explicit and is an ERROR where the grid is not"
                    "// editable: a column narrows, never widens. Omitted when absent." ]
                  "field:ColumnErased.Sortable",
                  [ "// Phase 861 — per-column sort NARROWING on the bound path (Phase 860's"
                    "// charter rule: a column flag narrows a behaviour, never widens it)."
                    "// Absent = inherit, i.e. sortable iff the column has a `field` and the"
                    "// grid declares `sortStateKey`. `false` opts this column out. `true` is"
                    "// the inherited default made explicit and is an ERROR where the grid"
                    "// declares no `sortStateKey` — a column cannot turn on a behaviour whose"
                    "// state key does not exist. Omitted on the wire when absent." ]
                  "field:DataGridSpec.DefaultSort",
                  [ "// Phase 861 — the bound path's declared INITIAL order, reusing the same"
                    "// `DefaultSort` record and field name `staticRows` carries (Phase 801):"
                    "// same behaviour, same spelling. It applies when the sort state key"
                    "// carries nothing yet; once the user has sorted, the state wins. A grid"
                    "// may declare it with no `sortStateKey` at all — an initial order"
                    "// without interactive re-sorting, exactly as a static table may." ]
                  "field:DataGridSpec.EditStateKey",
                  [ "// Phase 863 — the DECLARED edit destination: the State key an edited"
                    "// cell's whole updated rows value is committed to. Absent keeps Phase"
                    "// 663's shipped behaviour exactly — write back to the grid's own"
                    "// `source` when that source is a direct `Binding.State`, display-only"
                    "// otherwise. Present, it names the destination explicitly, which is what"
                    "// census row #27 asked for: a decoded editable grid could not say where"
                    "// its edits land, because the only spelling was a closure erasing to"
                    "// `\"<closure>\"`. Omitted on the wire when absent." ]
                  "field:DataGridSpec.KeepRowsTogether",
                  [ "// Phase 1473 — a ROW is one thing: when the rendering is paged, no row"
                    "// is split across the boundary, so a wrapped cell does not leave half"
                    "// its lines on one page and half on the next. `break-inside: avoid` on"
                    "// the row group's rows, and nothing at all on a continuous medium."
                    "//"
                    "// This is the half of the print-break vocabulary NO WRAPPER reaches. A"
                    "// `Box.keepTogether` around the grid keeps the whole grid together,"
                    "// which is why there is no grid-level keep-together slot; but nothing"
                    "// outside the grid knows where a row ends, so the boundary can only be"
                    "// declared here."
                    "//"
                    "// Omitted on the wire at `false`." ]
                  "field:DataGridSpec.PageSize",
                  [ "// Phase 862 — declarative pagination, the second instance of the"
                    "// grid-behaviour rule (Phase 860's charter): a behaviour the user drives"
                    "// names the State key the grid both writes and reads. `pageStateKey`"
                    "// carries the descriptor `{\"page\": <1-based int>}`; `pageSize` is how"
                    "// many rows a page holds. When both are set the runtime renders a pager"
                    "// and shows one page at a time; the pager is renderer-owned, so a"
                    "// decorative pager (a button writing state nothing reads) cannot be"
                    "// authored. Where the source is a `Query` whose `dependsOn` names the"
                    "// page key, the HOST pages and the grid does not slice. Both omitted on"
                    "// the wire when absent." ]
                  "field:DataGridSpec.Reorderable",
                  [ "// Phase 934 — declarative row reorder. Omit-when-false, matching its nearest"
                    "// sibling `editable`: for an affordance flag \"not stated\" and \"explicitly off\""
                    "// are the same state, so an option would carry a distinction the renderer"
                    "// cannot act on. The reordered rows commit to `editStateKey` above — a reorder"
                    "// IS a write of the whole updated rows value, so it needs no destination of"
                    "// its own." ]
                  "field:DataGridSpec.RepeatHeader",
                  [ "// Phase 1473 — the column headers repeat at the top of every page the"
                    "// grid continues onto, so a reader meeting the middle of a long grid on"
                    "// page four still knows what each column is. The header row group is"
                    "// projected as a TABLE HEADER GROUP, which is the one construct that"
                    "// makes the repetition the paged formatter's own job rather than"
                    "// script's — so it holds with no JavaScript at all."
                    "//"
                    "// Irreducible for the same reason `keepRowsTogether` is: the header is"
                    "// the grid's, and nothing outside it can name that row group."
                    "//"
                    "// Omitted on the wire at `false`." ]
                  "field:DataGridSpec.SortStateKey",
                  [ "// Phase 818 — the grid-sort header affordance for a DATA-BOUND grid:"
                    "// names the State key carrying the sort descriptor"
                    "// `{\"column\": <index>, \"direction\": \"asc\"|\"desc\"}`. When set, the"
                    "// runtime renders sortable column headers (a header click writes the"
                    "// toggled descriptor via the SetState path) and sorts its resolved rows"
                    "// by the state-carried descriptor before rendering. Sorting keys off the"
                    "// clicked column's `field` — a field-less closure column renders without"
                    "// the affordance. Omitted on the wire when absent; `staticRows`' own"
                    "// Phase-801 sort intent is untouched." ]
                  "type:ChartDataLabels",
                  [ "/// Whether a chart writes its values directly onto the picture, and where"
                    "/// (Phase 881)."
                    "///"
                    "/// A WIRE vocabulary, on the D8 line's semantic side: whether the reader is"
                    "/// meant to READ THE NUMBERS or read the shape is the author's meaning; the"
                    "/// type size, the offsets and the fit rule that realise it stay the host's, in"
                    "/// `ChartStyle`."
                    "///"
                    "/// THE CASE SET IS TWO, AND THAT IS THE POINT. There is deliberately no"
                    "/// \"all points\" case: a number on every interior point is the clutter this"
                    "/// vocabulary exists to avoid, so no shape of this API can request one. `Ends`"
                    "/// names the selective placements that read — a bar's cap, a line's last point"
                    "/// — and the set is closed there. Adding an all-points case later would not be"
                    "/// an extension; it would retract the guarantee." ]
                  "type:ChartLegendPosition",
                  [ "/// Which edge of the chart the series legend occupies — or `None`, which"
                    "/// suppresses it entirely (Phase 880)."
                    "///"
                    "/// A WIRE vocabulary, on the same side of the D8 line as `ChartSpec.Title`:"
                    "/// WHERE an author wants the legend is their meaning; the geometry that puts it"
                    "/// there — column widths, pitches, how the plot shrinks — stays the host's, in"
                    "/// `ChartStyle`. `ChartStyle.LegendPosition` carries the DEFAULT (`Right`); an"
                    "/// explicit `ChartSpec.LegendPosition` beats it."
                    "///"
                    "/// The case set is the four an author can mean. `Left` was declared by Phase 885"
                    "/// alongside the reserved field and is RETIRED here without ever having been"
                    "/// consumed by a lowering path: the left edge is the y axis's, so a legend there"
                    "/// competes with the tick column and the rotated axis title for the same band,"
                    "/// and the vocabulary charter's demand gate found no evidence for it. Retiring"
                    "/// an unconsumed case is cheaper than shipping a wire value that renders as a"
                    "/// guess." ]
                  "type:ChartXScale",
                  [ "/// What a chart's x axis MEANS — the scale its x column is read on (Phase 882)."
                    "///"
                    "/// A WIRE vocabulary on the D8 line's semantic side: whether a column is a set"
                    "/// of CATEGORIES or a run of DATES is a fact about the data the author is"
                    "/// declaring, not an appearance choice. Where the ticks land, how they are"
                    "/// formatted and how much margin they need are the host's, in `ChartStyle`."
                    "///"
                    "/// DECLARED, NOT INFERRED, and that is the point of the field. The chart's data"
                    "/// schema is statically known only for an embedded table with an empty pipeline,"
                    "/// so an inferred axis would make one wire tree draw a band axis or a temporal"
                    "/// one depending on where its rows came from; and sniffing the cell strings for"
                    "/// an ISO-8601 shape is a guess dressed as a rule. Declaring it lets the"
                    "/// pre-emit validator GROUND the claim instead (FUARAN097 refuses a temporal"
                    "/// axis over a non-date column) — the author says what the column is, and the"
                    "/// language refuses to be wrong about it quietly." ]
                  "type:DurationStyle",
                  [ "/// Phase 819 — presentation style for a duration: `Compact` \"1h 20m\","
                    "/// `Clock` \"1:20:00\", `Long` \"1 hour 20 minutes\"." ]
                  "type:DurationUnit",
                  [ "/// Phase 819 — how `Format.Duration` / `CellFormat.Duration` interpret the"
                    "/// numeric source: the unit the raw float counts." ]
                  "type:IconSize",
                  [ "/// Phase 821 — size class for the standalone `Icon` display kind; `Medium`"
                    "/// is the default and is omitted on the wire." ]
                  "type:IconSpec",
                  [ "/// Phase 821 — the standalone icon-only display kind: a decorative or"
                    "/// labelled glyph with no Button / Image envelope. `Icon` names a"
                    "/// glyph from the existing icon vocabulary (the `data-icon` hook); `Label ="
                    "/// None` is decorative (`aria-hidden=\"true\"`), `Some` is meaningful"
                    "/// (`role=\"img\"` + `aria-label`)." ]
                  "type:LinkProtection",
                  [ "/// Phase 812 — anti-scraper render strategy for a `Link`. `Email` marks a"
                    "/// `mailto:` link whose address must not appear in plaintext in emitted HTML"
                    "/// (the renderers own the emission strategy)." ] ]
        TypeSplice =
            Some
                """/// Phase 818 — a `Binding.Transform`'s source slot. `Data` is the
/// canonical columnar / `ref` source (the pre-818 shape, byte-identical on the
/// wire). `Live` preserves a binding-shaped source (State / Selection / Query)
/// verbatim so a runtime re-evaluates the Transform with subscription
/// semantics when the binding's channel changes; `initial` is the decode-time
/// snapshot table derived from the binding's carried default data (never
/// encoded — the binding IS the wire form), which SSR / diagnostic evaluation
/// reads, byte-identical to the Phase-815 snapshot for the same input.
and [<RequireQualifiedAccess>] TransformSource =
    | Data of source: Fuaran.Core.DataSource
    | Live of binding: Binding<JVal> * initial: Fuaran.Core.DataSource"""
        EncodeSplice =
            Some
                """// Phase 818 — a `Data` source keeps the Core columnar encoding byte-identical; a
// `Live` source re-encodes the preserved binding itself (one wire dialect — the
// State/Selection/Query-shaped source round-trips byte-for-byte; the derived
// `initial` snapshot is never encoded).
and private encTransformSource (s: TransformSource) : JVal =
    match s with
    | TransformSource.Data ds -> Fuaran.Core.ColumnCodec.encodeJson ds
    | TransformSource.Live (b, _) -> (encBinding id) b"""
        DecodeSplice =
            Some
                """// Phase 818 — the Transform source slot. A `$type` of State / Selection / Query preserves the binding as
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
    | _ -> asData j"""
        AccessorSplice =
            Some
                """// Phase 818 — JVal-level accessor for a data-shaped Action (the
// `encodeNodeKindJson` precedent): the server resume script re-encodes a
// `SetState` whose payload is a `valueFrom` Binding through the canonical
// encoder rather than growing a second hand-rolled binding encoder.
let encodeActionJson (a: Action<'Msg>) : JVal = encAction a"""
        CaseRefines =
            Map.ofList
                [ "Action.SetState",
                  """// Phase 818 — value XOR valueFrom (a literal, or a Binding
            // evaluated at dispatch time); exactly one must be present.
            match value, valueFrom with
            | Some _, Some _ -> Error "SetState carries both 'value' and 'valueFrom' — exactly one is allowed ('value' is a literal; 'valueFrom' derives the written value from a Binding at dispatch time)"
            | None, None -> Error "SetState requires 'value' (a literal JSON value) or 'valueFrom' (a Binding evaluated at dispatch time)"
            | _ -> Ok(Action.SetState(key, value, valueFrom))""" ]
        KindProjections = Map.ofList [ "Switch", switchProjection ] }

/// The declared-support DOCUMENT — the record above plus the host-prelude
/// declaration: members 2 and 3 of the regeneration triple, as one document
/// (`support.json`) beside member 1 (`idl.json`).
///
/// `Path` is relative to the document that declares it, so the triple resolves
/// wherever the domain is checked out. It names `src/Fuaran.UI/HostPrelude.fs`
/// because that is where the prelude has to live: it is compiled into `Fuaran.UI`
/// AHEAD of the generated module, so the generated code can reference the host
/// types and codecs the vocabulary's `THosted` slots and `TFn` placeholders name.
/// The generator never reads the prelude's text — knowing that one exists, what
/// module it declares and where it is, is the whole statement.
let document: SupportDocument =
    { Support = support
      HostPrelude =
        Some
            { Module = "Fuaran.UI.HostPrelude"
              Path = "../Fuaran.UI/HostPrelude.fs" } }
