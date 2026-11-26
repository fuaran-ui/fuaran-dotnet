namespace Fuaran.UI.Fragments

open Fuaran.Core
open Fuaran.UI
open Fuaran.UI.Types

// ============================================================================
//  The certified fragment library — APPLICATION-SPACE composition.
//
//  A curated set of `FragmentDecl`s for the composite shapes consumer apps
//  keep re-deriving: a labelled metric row, a KPI strip, a filter bar, a
//  confirm/cancel pair, an empty-state panel, a section header, a metric card,
//  a loading placeholder, a parameter panel, a two-scenario comparison and a
//  refinable table.
//  Each declares TYPED HOLES over bounded value-spaces and a two-axis EFFECT
//  CLASS, and each is certified valid-for-all-bindings by the verification
//  floor in `Fuaran.UI.Validator.RecipeCertification`.
//
//  WHAT THIS LIBRARY IS NOT — read `README.md` before adding to it. Fragments
//  are application-space vocabulary: reusable composition for consumer apps,
//  content packs, and pattern banks. They are NOT a second language vocabulary
//  and they never gate, substitute for, or pre-empt `NodeKind` admission. That
//  charter lives in `docs/VOCABULARY.md` and is a separate contract.
//
//  Each entry carries four things, and the separation between the last two is
//  the load-bearing part:
//
//   - `Decl` — the declaration node. Its `Body` is the TEMPLATE: value-hole
//     sites read `Binding.State(<holeName>, <default>)`, slot-hole sites are
//     unbound `FragmentRef` markers named for the slot. This is the shape the
//     renderer-side apply binds (`Fuaran.UI.Renderer.FragmentApply.apply`), and
//     the shape that travels on the wire.
//   - `Materialize` — the RECIPE-SPECIFIC step the certification harness drives
//     (Phase 359's `Materialize<'Msg>`): given a full binding of the value and
//     repeat holes, the emitted tree the fragment yields. It is a separate
//     function rather than a derivation of `Body` because a `Repeat` hole has no
//     in-tree expansion marker — nothing in the tier materialises a count — so
//     how a binding SHAPES the emission is knowledge only the fragment author
//     has. Slot markers are left standing: certification varies the value and
//     repeat holes only, and a slot's subtree is the caller's, not the
//     fragment's.
//   - `Example` — one representative applied `FragmentRef`, binding every
//     required hole. It is the fixture's applied half and the worked example a
//     reader copies.
//
//  FGP 2: `FSharp.Core` + `Fuaran.UI` only — Fable-clean, no renderer, ops,
//  validator or host dependency. The certification harness reaches IN to this
//  library from the test tier; nothing here reaches out.
// ============================================================================

/// One entry in the curated library.
type StdlibFragment<'Msg> =
    {
        /// The stable fragment name — what a `FragmentRefSpec.Name` targets.
        /// Permanent: renaming one breaks every consumer's refs and every
        /// stored tree that carries them.
        Name: string
        /// One line saying what composite shape this fragment stands for.
        Summary: string
        /// The declaration node (`NodeKind.FragmentDecl`), carrying the typed
        /// holes, the declared effect class, and the template body.
        Decl: Node<'Msg>
        /// The emitted tree for a full binding of the value / repeat holes —
        /// the certification harness's recipe-specific step. Slot markers are
        /// left in place.
        Materialize: Map<string, obj> -> Node<'Msg>
        /// A representative applied reference, binding every required hole.
        Example: Node<'Msg>
    }

/// The model a scenario comparison runs twice (Phase 2752): ONE function the
/// comparison's pipeline defines (`Transform.Define`), over the rows of `Source`.
///
/// The function's parameters split in two. Those named in `Columns` read a
/// column of each row; every other parameter is a SCENARIO parameter, which each
/// scenario binds to a value of its own. The comparison calls the function once
/// per scenario, so both scenarios run the same model and differ only in their
/// parameter values.
type ScenarioModel =
    {
        /// The rows: inline (`Embedded`) or a name the host resolves (`Ref`).
        Source: Fuaran.Core.DataSource
        /// The column naming each row — the grid's row key and its first column.
        Key: string
        /// The per-row model. Its body reads its parameters through `Arg`.
        Model: Fuaran.Compute.FnDef
        /// `(function parameter, source column)` — the parameters a row feeds.
        Columns: (string * string) list
    }

/// One scenario of a comparison (Phase 2752): its column heading, and where it
/// reads each scenario parameter of the model — a literal, a hole's state slot,
/// or a `Binding.Filter` a `parameter-panel` control writes.
type ScenarioSide =
    { Label: string
      Parameters: (string * Binding<JVal>) list }

/// How a refinable table's column compares when the reader sorts it (Phase
/// 2753): the order the grid applies to the column's cells, which the
/// substituted pipeline must reproduce. `Text` folds case, as the grid does.
[<RequireQualifiedAccess>]
type RefinableKind =
    | Text
    | Number

/// One column of a refinable table (Phase 2753).
type RefinableColumn =
    {
        /// The row column the grid column displays.
        Field: string
        /// The column heading.
        Label: string
        Kind: RefinableKind
        /// Whether the column's header sorts the rows.
        Sortable: bool
        /// The membership filter over the column: `Some options` puts a token
        /// chip in the table's filter bar, suggesting `options`; `None` leaves
        /// the column unfiltered.
        Filter: SelectOption list option
    }

/// A table whose view a reader refines (Phase 2753): rows, the column naming
/// each row, the columns in display order, and the page size.
type RefinableTable =
    { Source: Fuaran.Core.DataSource
      Key: string
      Columns: RefinableColumn list
      PageSize: int }

/// A refinable table's current view (Phase 2753), as the host reads it from
/// the table's own state keys: the grid's effective sort descriptor (a column
/// index into `Columns` and a direction — `effectiveSortDescriptor`'s reading),
/// the page the grid shows (1-based and already clamped, as the grid's page
/// rule clamps it), and each membership filter's selection by column field. An
/// empty selection is no constraint.
type RefinableView =
    { Sort: (int * Fuaran.UI.Types.SortDirection) option
      Page: int
      Filters: (string * string list) list }

module Stdlib =

    // ── Naming ──────────────────────────────────────────────────────────────
    //
    // Node ids are stable and prefixed so the corpus fixtures group with the
    // rest of the `frag-*` family. A decl's id is also the hole ADDRESS prefix
    // certification uses (`<declId>.<holeName>`), so it is not cosmetic.

    let private declIdOf (name: string) : string = "frag-stdlib-" + name

    let private refIdOf (name: string) : string = "frag-stdlib-" + name + "-ref"

    // ── Binding readers ─────────────────────────────────────────────────────
    //
    // The certification harness hands `Materialize` a `Map<string, obj>` whose
    // values are boxed per the hole's value-space (`IntRange` → int,
    // `FloatRange` → float, everything else → string). Each reader falls back
    // to the hole's declared default so a materialiser is TOTAL even when the
    // harness has not bound an optional hole.

    let private readStr (fallback: string) (hole: string) (b: Map<string, obj>) : string =
        match Map.tryFind hole b with
        | Some(:? string as s) -> s
        | _ -> fallback

    let private readNum (fallback: float) (hole: string) (b: Map<string, obj>) : float =
        match Map.tryFind hole b with
        | Some(:? float as f) -> f
        | Some(:? int as i) -> float i
        | _ -> fallback

    let private readCount (fallback: int) (hole: string) (b: Map<string, obj>) : int =
        match Map.tryFind hole b with
        | Some(:? int as n) -> n
        | Some(:? float as f) -> int f
        | _ -> fallback

    // ── Shorthand ───────────────────────────────────────────────────────────

    let private pureDeterministic: EffectClass =
        { HostEffect = HostEffect.Pure
          Determinism = DeterminismSource.Deterministic }

    let private readsHost: EffectClass =
        { HostEffect = HostEffect.ReadsHost
          Determinism = DeterminismSource.Deterministic }

    let private writesHost: EffectClass =
        { HostEffect = HostEffect.WritesHost
          Determinism = DeterminismSource.Deterministic }

    /// A value-hole site in a TEMPLATE body: the text reads the hole's state
    /// slot, falling back to the hole's declared default.
    let private holeText (hole: string) (fallback: string) : TextSource =
        TextSource.Bound(Binding.State(hole, Some fallback))

    /// A numeric value-hole site in a template body.
    let private holeNumber (hole: string) (fallback: float) : Binding<float> = Binding.State(hole, Some fallback)

    /// A tree-slot site in a template body: an unbound `FragmentRef` named for
    /// the slot, which `FragmentApply` replaces with the bound subtree.
    let private slotMarker<'Msg> (id: string) (slot: string) : Node<'Msg> = Fuaran.fragmentRef id slot

    /// Build a declaration in the CANONICAL wire shape.
    ///
    /// Both optional fields carry a degenerate form the wire format specifies as
    /// the one form for that meaning: a zero-hole decl omits `holes`, and — the
    /// one that is easy to get wrong — a **pure-deterministic decl omits
    /// `effect`** (`WIRE_FORMAT.md`, "Parameterised fragments": "Omitted when
    /// pure-deterministic"). The F# type carries `Effect` as an option and
    /// encodes `Some x` verbatim, so nothing here stops an author writing the
    /// redundant explicit default — it decodes to the same meaning, and the F#
    /// host round-trips it happily.
    ///
    /// It is still WRONG, and this is not a stylistic preference: a host that
    /// normalises to the specified form re-encodes the redundant default away
    /// and its byte-comparison fails. Caught exactly that way — the first cut of
    /// these fixtures wrote `Some pureDeterministic` and six of them broke the
    /// Rust host's conformance leg while the F# one stayed green, because F# is
    /// the encoder that produced the bytes it was checking against. `Stdlib.all`
    /// pins the rule in the suite so the shape cannot come back.
    let private decl<'Msg> (name: string) (body: Node<'Msg>) (holes: HoleDecl list) (effect: EffectClass) : Node<'Msg> =
        Fuaran.fragmentDecl
            (declIdOf name)
            { Name = name
              Body = body
              // Phase 1670 — the canonicalisation this library used to perform
              // by hand is the FIELD'S rule now (omit-at-default), so both slots
              // carry the value and the encoder decides whether it reaches the
              // wire. The library's discipline became the host's, which is where
              // the original note said it belonged.
              Holes = holes
              Effect = effect }

    /// Apply a fragment: a `FragmentRef` at a caller-chosen node id, binding the
    /// given holes.
    ///
    /// **The node id is the hygiene prefix**, not decoration. `FragmentApply`
    /// namespaces an inserted slot subtree's ids by the ref site and keys each
    /// bound value at `<refId>.<holeName>`, so two applications of one fragment
    /// on the same page keep their slots and their values apart precisely
    /// because their ids differ. Giving both the same id is how they capture
    /// each other.
    ///
    /// The declaration must be present in the emitted tree for the ref to
    /// resolve — declare once near the root, apply as often as you like.
    let apply<'Msg> (nodeId: string) (name: string) (args: (string * FragmentArg<'Msg>) list) : Node<'Msg> =
        { Fuaran.fragmentRef nodeId name with
            Kind =
                NodeKind.FragmentRef
                    { Name = name
                      Args =
                        (if List.isEmpty args then
                             Option.None
                         else
                             Some(Map.ofList args)) } }

    /// The `Example` application, at the fragment's own reserved ref id.
    let private reference<'Msg> (name: string) (args: (string * FragmentArg<'Msg>) list) : Node<'Msg> =
        apply (refIdOf name) name args

    // ════════════════════════════════════════════════════════════════════════
    //  1. labelled-metric-row — a label and its figure on one line.
    // ════════════════════════════════════════════════════════════════════════

    [<Literal>]
    let LabelledMetricRow = "labelled-metric-row"

    let private labelledMetricRowHoles: HoleDecl list =
        [ HoleDecl.Value("label", HoleValueSpace.StringLen(1, 48), Some(Scalar.Str "Metric"))
          HoleDecl.Value("value", HoleValueSpace.FloatRange(-1_000_000.0, 1_000_000.0), Option.None) ]

    let private labelledMetricRowBody<'Msg> : Node<'Msg> =
        Fuaran.labelValueRow
            "labelled-metric-row-row"
            { Defaults.labelValueRow with
                Label = holeText "label" "Metric"
                Value = holeNumber "value" 0.0
                Format = CellFormat.Number(Some 0, None) }

    let private labelledMetricRowMaterialize<'Msg> : Map<string, obj> -> Node<'Msg> =
        fun b ->
            Fuaran.labelValueRow
                "labelled-metric-row-row"
                { Defaults.labelValueRow with
                    Label = TextSource.Literal(readStr "Metric" "label" b)
                    Value = Binding.Static(Some(readNum 0.0 "value" b))
                    Format = CellFormat.Number(Some 0, None) }

    let labelledMetricRow<'Msg> : StdlibFragment<'Msg> =
        { Name = LabelledMetricRow
          Summary = "A label and its figure on one line — the smallest read-only composite in the set."
          Decl = decl LabelledMetricRow labelledMetricRowBody labelledMetricRowHoles pureDeterministic
          Materialize = labelledMetricRowMaterialize
          Example =
            reference LabelledMetricRow [ "label", FragmentArg.Str "Open incidents"; "value", FragmentArg.Float 42.0 ] }

    // ════════════════════════════════════════════════════════════════════════
    //  2. kpi-strip — a headed row of N figures.
    // ════════════════════════════════════════════════════════════════════════

    [<Literal>]
    let KpiStrip = "kpi-strip"

    /// The most figures a strip carries — the `count` hole's upper bound, and
    /// the number of per-figure hole triples the declaration carries.
    [<Literal>]
    let private KpiMaxFigures = 6

    // Phase 2471 — the per-figure holes. A strip of real figures mixes money,
    // counts and rates, and compares each against a prior period, so a figure's
    // FORMAT, its TREND and the trend's POLARITY are holes rather than fixed.
    // Each is an `Enum` over a closed token set (a bounded value-space, so the
    // fragment stays total), and each defaults to exactly what the strip did
    // before the holes existed: `Number(Some 0)`, no trend, higher is better. A
    // reference that binds none of them therefore expands as it always has.
    //
    // The tokens project the `CellFormat` vocabulary a strip figure needs, not
    // the whole of it: a date, a duration or a host formatter is not a KPI
    // figure's shape. `currency` reads its ISO code from the strip-wide
    // `currency` hole — one strip, one currency.

    let private kpiFormatTokens =
        [ "number"
          "number-0"
          "number-1"
          "number-2"
          "percent"
          "percent-0"
          "percent-1"
          "percent-2"
          "currency" ]

    /// The trend tokens: `none` (no trend), `value` (the trend reads in the
    /// figure's own format), or a percent format for a relative change.
    let private kpiTrendTokens =
        [ "none"; "value"; "percent-0"; "percent-1"; "percent-2" ]

    let private kpiPolarityTokens = [ "higher-is-better"; "lower-is-better" ]

    let private kpiFormatOf (currency: string) (token: string) : CellFormat =
        match token with
        | "number" -> CellFormat.Number(Option.None, Option.None)
        | "number-1" -> CellFormat.Number(Some 1, None)
        | "number-2" -> CellFormat.Number(Some 2, None)
        | "percent" -> CellFormat.Percent(Option.None, Option.None)
        | "percent-0" -> CellFormat.Percent(Some 0, None)
        | "percent-1" -> CellFormat.Percent(Some 1, None)
        | "percent-2" -> CellFormat.Percent(Some 2, None)
        // A blank code is FUARAN061; the hole's space admits whitespace, so the
        // materialiser falls back to the default rather than emit the defect.
        | "currency" ->
            CellFormat.Currency(
                (if System.String.IsNullOrWhiteSpace currency then
                     "USD"
                 else
                     currency),
                Option.None,
                Option.None
            )
        | _ -> CellFormat.Number(Some 0, None) // "number-0", the default

    /// `None` = no trend; `Some None` = the trend in the figure's own format;
    /// `Some(Some f)` = the trend in `f`.
    let private kpiTrendOf (token: string) : CellFormat option option =
        match token with
        | "value" -> Some Option.None
        | "percent-0" -> Some(Some(CellFormat.Percent(Some 0, None)))
        | "percent-1" -> Some(Some(CellFormat.Percent(Some 1, None)))
        | "percent-2" -> Some(Some(CellFormat.Percent(Some 2, None)))
        | _ -> Option.None // "none", the default

    let private kpiPolarityOf (token: string) : TrendPolarity =
        match token with
        | "lower-is-better" -> TrendPolarity.LowerIsBetter
        | _ -> TrendPolarity.HigherIsBetter

    let private kpiStripHoles: HoleDecl list =
        [ HoleDecl.Value("heading", HoleValueSpace.StringLen(1, 40), Some(Scalar.Str "Key figures"))
          // TOTALITY (invariant 1): the count is a bounded `IntRange`, so no
          // binding can produce unbounded expansion.
          HoleDecl.Repeat("count", HoleValueSpace.IntRange(1, KpiMaxFigures))
          HoleDecl.Value("currency", HoleValueSpace.StringLen(3, 3), Some(Scalar.Str "USD")) ]
        @ [ for i in 1..KpiMaxFigures do
                HoleDecl.Value(sprintf "format-%d" i, HoleValueSpace.Enum kpiFormatTokens, Some(Scalar.Str "number-0"))
                HoleDecl.Value(sprintf "trend-%d" i, HoleValueSpace.Enum kpiTrendTokens, Some(Scalar.Str "none"))

                HoleDecl.Value(
                    sprintf "polarity-%d" i,
                    HoleValueSpace.Enum kpiPolarityTokens,
                    Some(Scalar.Str "higher-is-better")
                ) ]

    /// One figure. Its label, value and (when it has one) trend read host state
    /// at `kpi-<i>-label` / `kpi-<i>-value` / `kpi-<i>-trend`: the figures are
    /// data, the shape is the fragment's.
    let private kpiMetric<'Msg>
        (i: int)
        (format: CellFormat)
        (trend: CellFormat option option)
        (polarity: TrendPolarity)
        : Node<'Msg> =
        Fuaran.metric
            (sprintf "kpi-strip-metric-%d" i)
            { Defaults.metric with
                Label = TextSource.Bound(Binding.State(sprintf "kpi-%d-label" i, Some(sprintf "KPI %d" i)))
                Value = Binding.State(sprintf "kpi-%d-value" i, Some 0.0)
                Format = format
                Trend =
                    trend
                    |> Option.map (fun _ -> Binding.State(sprintf "kpi-%d-trend" i, Option.None))
                TrendFormat = Option.flatten trend
                TrendPolarity = polarity }

    /// The figure every hole default describes — what the strip emitted before
    /// the per-figure holes existed.
    let private kpiDefaultMetric<'Msg> (i: int) : Node<'Msg> =
        kpiMetric i (CellFormat.Number(Some 0, None)) Option.None TrendPolarity.HigherIsBetter

    let private kpiStripTree<'Msg> (heading: string) (figures: Node<'Msg> list) : Node<'Msg> =
        Fuaran.stack
            "kpi-strip"
            { Defaults.stack with
                Orientation = Orientation.Horizontal
                Children =
                    Fuaran.heading
                        "kpi-strip-heading"
                        { Defaults.heading with
                            Level = 3
                            Text = TextSource.Literal heading
                            Variant = HeadingVariant.Eyebrow }
                    :: figures }

    let private kpiStripBody<'Msg> : Node<'Msg> =
        // The template's heading reads its hole; the exemplar figure stands for
        // the repeat, which nothing in the tier expands in-tree.
        Fuaran.stack
            "kpi-strip"
            { Defaults.stack with
                Orientation = Orientation.Horizontal
                Children =
                    [ Fuaran.heading
                          "kpi-strip-heading"
                          { Defaults.heading with
                              Level = 3
                              Text = holeText "heading" "Key figures"
                              Variant = HeadingVariant.Eyebrow }
                      kpiDefaultMetric 1 ] }

    let private kpiStripMaterialize<'Msg> : Map<string, obj> -> Node<'Msg> =
        fun b ->
            let currency = readStr "USD" "currency" b

            let figure i =
                kpiMetric
                    i
                    (kpiFormatOf currency (readStr "number-0" (sprintf "format-%d" i) b))
                    (kpiTrendOf (readStr "none" (sprintf "trend-%d" i) b))
                    (kpiPolarityOf (readStr "higher-is-better" (sprintf "polarity-%d" i) b))

            kpiStripTree (readStr "Key figures" "heading" b) [ for i in 1 .. readCount 1 "count" b -> figure i ]

    let kpiStrip<'Msg> : StdlibFragment<'Msg> =
        { Name = KpiStrip
          Summary = "A headed horizontal strip of one to six figures — the dashboard's top band."
          Decl = decl KpiStrip kpiStripBody kpiStripHoles pureDeterministic
          Materialize = kpiStripMaterialize
          Example = reference KpiStrip [ "heading", FragmentArg.Str "This week"; "count", FragmentArg.Int 3 ] }

    // ════════════════════════════════════════════════════════════════════════
    //  3. filter-bar — a row of filter chips over host filter state.
    // ════════════════════════════════════════════════════════════════════════

    [<Literal>]
    let FilterBar = "filter-bar"

    /// The most choice filters a bar carries — the `choices` hole's bound.
    [<Literal>]
    let private FilterBarMaxChoices = 4

    // Phase 2471 — the chips are a bounded repeat of CHOICE filters, each with a
    // label, the host filter key it writes and its options. `choices` = 0 (the
    // default) keeps the original bar: a search chip and a status chip. A
    // `search` hole keeps or drops the free-text chip either way.
    //
    // `choices` is a Value hole over `IntRange(0, 4)` rather than a `Repeat`:
    // a repeat is always required, and making this one required would refuse
    // every existing reference, which binds none of it. The bound is what
    // totality asks of a count, and the `IntRange` carries it.
    //
    // A hole is a scalar, so a chip's options travel as ONE bounded string in a
    // small total grammar: entries separated by `|`, each `value=Label`, or a
    // bare `Label` whose value is the label itself. `=Both sites` is an option
    // with the empty value. Every string parses: an entry with a blank label is
    // dropped, and a string with no usable entry yields one `All` option, so no
    // binding can produce an option-less chip.

    let private filterBarHoles: HoleDecl list =
        [ HoleDecl.Value("searchLabel", HoleValueSpace.StringLen(1, 32), Some(Scalar.Str "Search"))
          HoleDecl.Value("statusLabel", HoleValueSpace.StringLen(1, 32), Some(Scalar.Str "Status"))
          HoleDecl.Value("search", HoleValueSpace.Enum [ "shown"; "hidden" ], Some(Scalar.Str "shown"))
          HoleDecl.Value("choices", HoleValueSpace.IntRange(0, FilterBarMaxChoices), Some(Scalar.Int 0)) ]
        @ [ for k in 1..FilterBarMaxChoices do
                HoleDecl.Value(
                    sprintf "choice-%d-label" k,
                    HoleValueSpace.StringLen(1, 32),
                    Some(Scalar.Str(sprintf "Filter %d" k))
                )

                HoleDecl.Value(
                    sprintf "choice-%d-key" k,
                    HoleValueSpace.StringLen(1, 32),
                    Some(Scalar.Str(sprintf "filter-%d" k))
                )

                HoleDecl.Value(sprintf "choice-%d-options" k, HoleValueSpace.StringLen(1, 240), Some(Scalar.Str "=All")) ]

    /// Parse a filter-bar chip's options string — entries separated by `|`, each
    /// `value=Label` or a bare `Label` (its own value). Total: an entry with a
    /// blank label is dropped, and a string with no usable entry yields a single
    /// `All` option with the empty value.
    let parseFilterOptions (raw: string) : SelectOption list =
        let options =
            raw.Split '|'
            |> Array.toList
            |> List.choose (fun entry ->
                let value, label =
                    match entry.IndexOf '=' with
                    | -1 -> entry, entry
                    | at -> entry.Substring(0, at), entry.Substring(at + 1)

                if System.String.IsNullOrWhiteSpace label then
                    Option.None
                else
                    Some(
                        { Label = TextSource.Literal label
                          Value = value }
                        : SelectOption
                    ))

        if List.isEmpty options then
            [ ({ Label = TextSource.Literal "All"
                 Value = "" }
              : SelectOption) ]
        else
            options

    let private statusOptions: Binding<SelectOption list> =
        Binding.Static(
            Some
                [ { Label = TextSource.Literal "Open"
                    Value = "open" }
                  { Label = TextSource.Literal "Closed"
                    Value = "closed" } ]
        )

    let private searchChip<'Msg> (label: TextSource) : FilterSpec<'Msg> =
        { Name = "search"
          Label = label
          Kind = FilterField.text "search" }

    let private choiceChip<'Msg>
        (key: string)
        (label: TextSource)
        (options: Binding<SelectOption list>)
        : FilterSpec<'Msg> =
        { Name = key
          Label = label
          Kind = FilterField.choice key options }

    let private filterBarTree<'Msg> (chips: FilterSpec<'Msg> list) : Node<'Msg> = Fuaran.filters "filter-bar" chips

    let private filterBarMaterialize<'Msg> : Map<string, obj> -> Node<'Msg> =
        fun b ->
            let search =
                if readStr "shown" "search" b = "hidden" then
                    []
                else
                    [ searchChip (TextSource.Literal(readStr "Search" "searchLabel" b)) ]

            let choiceCount = min (readCount 0 "choices" b) FilterBarMaxChoices

            let choices =
                if choiceCount <= 0 then
                    [ choiceChip "status" (TextSource.Literal(readStr "Status" "statusLabel" b)) statusOptions ]
                else
                    [ for k in 1..choiceCount ->
                          choiceChip
                              (readStr (sprintf "filter-%d" k) (sprintf "choice-%d-key" k) b)
                              (TextSource.Literal(readStr (sprintf "Filter %d" k) (sprintf "choice-%d-label" k) b))
                              (Binding.Static(
                                  Some(parseFilterOptions (readStr "=All" (sprintf "choice-%d-options" k) b))
                              )) ]

            filterBarTree (search @ choices)

    let filterBar<'Msg> : StdlibFragment<'Msg> =
        { Name = FilterBar
          Summary =
            "A row of filter chips, each bound to its own host filter key: a free-text chip and a status chip by default, or up to four choice filters."
          Decl =
            decl
                FilterBar
                (filterBarTree
                    [ searchChip (holeText "searchLabel" "Search")
                      choiceChip "status" (holeText "statusLabel" "Status") statusOptions ])
                filterBarHoles
                // The chips READ host filter state. Certification of an
                // effecting fragment asserts STRUCTURE only (Phase 52) — never
                // that the emission is a pure function of its holes.
                readsHost
          Materialize = filterBarMaterialize
          Example =
            reference
                FilterBar
                [ "searchLabel", FragmentArg.Str "Find an incident"
                  "statusLabel", FragmentArg.Str "State" ] }

    // ════════════════════════════════════════════════════════════════════════
    //  4. confirm-action-pair — a confirm / cancel pair over one state key.
    // ════════════════════════════════════════════════════════════════════════

    [<Literal>]
    let ConfirmActionPair = "confirm-action-pair"

    /// The state key both buttons write. Fixed rather than held as a hole: two
    /// refs of this fragment on one page want two INDEPENDENT answers, and the
    /// hygienic apply already namespaces value-hole addresses by the ref site —
    /// so the key that keeps them apart is the ref's, not the author's.
    [<Literal>]
    let ConfirmStateKey = "confirm-action-pair.confirmed"

    let private confirmActionPairHoles: HoleDecl list =
        [ HoleDecl.Value("confirmLabel", HoleValueSpace.StringLen(1, 24), Some(Scalar.Str "Confirm"))
          HoleDecl.Value("cancelLabel", HoleValueSpace.StringLen(1, 24), Some(Scalar.Str "Cancel")) ]

    let private confirmActionPairTree<'Msg> (confirmLabel: TextSource) (cancelLabel: TextSource) : Node<'Msg> =
        Fuaran.stack
            "confirm-action-pair"
            { Defaults.stack with
                Orientation = Orientation.Horizontal
                Children =
                    [ Fuaran.button
                          "confirm-action-pair-confirm"
                          { Defaults.button with
                              Label = confirmLabel
                              Variant = ButtonVariant.Primary
                              OnClick = Action.SetState(ConfirmStateKey, Some(JBool true), Option.None) }
                      Fuaran.button
                          "confirm-action-pair-cancel"
                          { Defaults.button with
                              Label = cancelLabel
                              Variant = ButtonVariant.Secondary
                              OnClick = Action.SetState(ConfirmStateKey, Some(JBool false), Option.None) } ] }

    let confirmActionPair<'Msg> : StdlibFragment<'Msg> =
        { Name = ConfirmActionPair
          Summary = "A primary confirm and a secondary cancel, both writing one declared state key."
          Decl =
            decl
                ConfirmActionPair
                (confirmActionPairTree (holeText "confirmLabel" "Confirm") (holeText "cancelLabel" "Cancel"))
                confirmActionPairHoles
                // Both buttons WRITE host state — structure-only certification.
                writesHost
          Materialize =
            fun b ->
                confirmActionPairTree
                    (TextSource.Literal(readStr "Confirm" "confirmLabel" b))
                    (TextSource.Literal(readStr "Cancel" "cancelLabel" b))
          Example =
            reference
                ConfirmActionPair
                [ "confirmLabel", FragmentArg.Str "Delete report"
                  "cancelLabel", FragmentArg.Str "Keep it" ] }

    // ════════════════════════════════════════════════════════════════════════
    //  5. empty-state-panel — the nothing-to-show panel with a caller's action.
    // ════════════════════════════════════════════════════════════════════════

    [<Literal>]
    let EmptyStatePanel = "empty-state-panel"

    let private emptyStatePanelHoles: HoleDecl list =
        [ HoleDecl.Value("title", HoleValueSpace.StringLen(1, 60), Some(Scalar.Str "Nothing here yet"))
          HoleDecl.Value(
              "body",
              HoleValueSpace.StringLen(1, 160),
              Some(Scalar.Str "There is nothing to show for the current selection.")
          )
          // A TREE slot, kind-constrained to a Button: the caller supplies the
          // way out of the empty state. `FragmentApply` enforces the constraint
          // at bind time.
          HoleDecl.Slot("action", Some "Button") ]

    let private emptyStatePanelTree<'Msg> (title: TextSource) (body: TextSource) : Node<'Msg> =
        Fuaran.card
            "empty-state-panel"
            { Defaults.card with
                Children =
                    [ Fuaran.heading
                          "empty-state-panel-title"
                          { Defaults.heading with
                              Level = 3
                              Text = title }
                      Fuaran.markdownSpec "empty-state-panel-body" { Citations = []; Text = body }
                      // The slot marker stands in both the template and the
                      // materialised emission: certification varies value holes
                      // only, and the subtree here belongs to the caller.
                      slotMarker "empty-state-panel-action" "action" ] }

    let emptyStatePanel<'Msg> : StdlibFragment<'Msg> =
        { Name = EmptyStatePanel
          Summary = "A titled nothing-to-show card whose call to action is a caller-supplied Button slot."
          Decl =
            decl
                EmptyStatePanel
                (emptyStatePanelTree
                    (holeText "title" "Nothing here yet")
                    (holeText "body" "There is nothing to show for the current selection."))
                emptyStatePanelHoles
                pureDeterministic
          Materialize =
            fun b ->
                emptyStatePanelTree
                    (TextSource.Literal(readStr "Nothing here yet" "title" b))
                    (TextSource.Literal(readStr "There is nothing to show for the current selection." "body" b))
          Example =
            reference
                EmptyStatePanel
                [ "title", FragmentArg.Str "No incidents"
                  "action",
                  FragmentArg.SlotArg(
                      Fuaran.button
                          "raise"
                          { Defaults.button with
                              Label = TextSource.Literal "Raise one"
                              Variant = ButtonVariant.Primary }
                  ) ] }

    // ════════════════════════════════════════════════════════════════════════
    //  6. section-header — eyebrow, title, and the heading level as a hole.
    // ════════════════════════════════════════════════════════════════════════

    [<Literal>]
    let SectionHeader = "section-header"

    let private sectionHeaderHoles: HoleDecl list =
        [ HoleDecl.Value("eyebrow", HoleValueSpace.StringLen(1, 32), Some(Scalar.Str "Section"))
          HoleDecl.Value("title", HoleValueSpace.StringLen(1, 80), Option.None)
          // The heading LEVEL is a bounded int hole, so a section header placed
          // deeper in a document keeps its document outline correct without a
          // second fragment.
          HoleDecl.Value("level", HoleValueSpace.IntRange(1, 4), Some(Scalar.Int 2)) ]

    let private sectionHeaderTree<'Msg> (eyebrow: TextSource) (title: TextSource) (level: int) : Node<'Msg> =
        Fuaran.stack
            "section-header"
            { Defaults.stack with
                Children =
                    [ Fuaran.heading
                          "section-header-eyebrow"
                          { Defaults.heading with
                              Level = level
                              Text = eyebrow
                              Variant = HeadingVariant.Eyebrow }
                      Fuaran.heading
                          "section-header-title"
                          { Defaults.heading with
                              Level = level
                              Text = title
                              Variant = HeadingVariant.Standard } ] }

    let sectionHeader<'Msg> : StdlibFragment<'Msg> =
        { Name = SectionHeader
          Summary = "An eyebrow above a title, at a caller-chosen heading level."
          Decl =
            decl
                SectionHeader
                (sectionHeaderTree (holeText "eyebrow" "Section") (holeText "title" "") 2)
                sectionHeaderHoles
                pureDeterministic
          Materialize =
            fun b ->
                sectionHeaderTree
                    (TextSource.Literal(readStr "Section" "eyebrow" b))
                    (TextSource.Literal(readStr "Untitled" "title" b))
                    (readCount 2 "level" b)
          Example =
            reference
                SectionHeader
                [ "eyebrow", FragmentArg.Str "Operations"
                  "title", FragmentArg.Str "Incident summary"
                  "level", FragmentArg.Int 2 ] }

    // ════════════════════════════════════════════════════════════════════════
    //  7. metric-card — one headline figure with a caption, in a card.
    // ════════════════════════════════════════════════════════════════════════

    [<Literal>]
    let MetricCard = "metric-card"

    let private metricCardHoles: HoleDecl list =
        [ HoleDecl.Value("title", HoleValueSpace.StringLen(1, 48), Some(Scalar.Str "Summary"))
          HoleDecl.Value("value", HoleValueSpace.FloatRange(-1_000_000.0, 1_000_000.0), Option.None)
          HoleDecl.Value("caption", HoleValueSpace.StringLen(1, 80), Some(Scalar.Str "vs. previous period")) ]

    let private metricCardTree<'Msg> (title: TextSource) (value: Binding<float>) (caption: TextSource) : Node<'Msg> =
        Fuaran.card
            "metric-card"
            { Defaults.card with
                Heading = Some title
                Children =
                    [ Fuaran.metric
                          "metric-card-value"
                          { Defaults.metric with
                              Label = title
                              Value = value
                              Format = CellFormat.Number(Some 0, None) }
                      Fuaran.markdownSpec "metric-card-caption" { Citations = []; Text = caption } ] }

    let metricCard<'Msg> : StdlibFragment<'Msg> =
        { Name = MetricCard
          Summary = "A card carrying one headline figure and the caption that qualifies it."
          Decl =
            decl
                MetricCard
                (metricCardTree
                    (holeText "title" "Summary")
                    (holeNumber "value" 0.0)
                    (holeText "caption" "vs. previous period"))
                metricCardHoles
                pureDeterministic
          Materialize =
            fun b ->
                metricCardTree
                    (TextSource.Literal(readStr "Summary" "title" b))
                    (Binding.Static(Some(readNum 0.0 "value" b)))
                    (TextSource.Literal(readStr "vs. previous period" "caption" b))
          Example =
            reference
                MetricCard
                [ "title", FragmentArg.Str "Mean time to resolve"
                  "value", FragmentArg.Float 137.0
                  "caption", FragmentArg.Str "minutes, vs. 162 last month" ] }

    // ════════════════════════════════════════════════════════════════════════
    //  8. loading-placeholder — N skeleton rows while a query is in flight.
    // ════════════════════════════════════════════════════════════════════════

    [<Literal>]
    let LoadingPlaceholder = "loading-placeholder"

    let private loadingPlaceholderHoles: HoleDecl list =
        [ HoleDecl.Repeat("rows", HoleValueSpace.IntRange(1, 8)) ]

    let private loadingPlaceholderTree<'Msg> (rows: int) : Node<'Msg> =
        Fuaran.skeleton "loading-placeholder" rows

    let loadingPlaceholder<'Msg> : StdlibFragment<'Msg> =
        { Name = LoadingPlaceholder
          Summary = "One to eight skeleton rows — the shape a list takes while its query is in flight."
          Decl = decl LoadingPlaceholder (loadingPlaceholderTree 3) loadingPlaceholderHoles pureDeterministic
          Materialize = fun b -> loadingPlaceholderTree (readCount 3 "rows" b)
          Example = reference LoadingPlaceholder [ "rows", FragmentArg.Int 4 ] }

    // ════════════════════════════════════════════════════════════════════════
    //  9. parameter-panel — the controls that set a pipeline's parameters.
    // ════════════════════════════════════════════════════════════════════════

    [<Literal>]
    let ParameterPanel = "parameter-panel"

    /// The most parameters a panel carries — the `count` hole's upper bound.
    [<Literal>]
    let private PanelMaxParameters = 4

    // Phase 2752. A pipeline reads named parameters, and a `TransformParam` binds
    // each one to a source. This panel is the control side of that: parameter
    // `<name>` is a filter chip writing host filter key `<name>`, and a pipeline
    // reads it with `{ Name = "<name>"; From = Binding.Filter("<name>", …) }`.
    //
    // A FILTER key rather than a free state key, deliberately. A chip declares its
    // name in the tree, so pre-emit validation can hold the panel and its readers
    // to each other: a control naming a parameter no pipeline reads is FUARAN074
    // (a decorative filter), and a pipeline reading a parameter no control
    // declares is FUARAN075. A state key carries no declaration and no such check.
    //
    // A parameter is a number (a `RangedNumber` over `min`..`max` in steps of
    // `step`, starting at `default`) or a choice (options in the filter-bar's
    // `value=Label|…` grammar). Every binding materialises: bounds that arrive in
    // the wrong order are swapped, a default outside them is clamped, a blank
    // name falls back to `param-N`, and a name already taken drops the later
    // control rather than emit two writers of one key.

    let private panelControlTokens = [ "number"; "choice" ]

    let private panelHoles: HoleDecl list =
        [ HoleDecl.Value("heading", HoleValueSpace.StringLen(1, 40), Some(Scalar.Str "Parameters"))
          HoleDecl.Repeat("count", HoleValueSpace.IntRange(1, PanelMaxParameters)) ]
        @ [ for k in 1..PanelMaxParameters do
                HoleDecl.Value(
                    sprintf "param-%d-name" k,
                    HoleValueSpace.StringLen(1, 32),
                    Some(Scalar.Str(sprintf "param-%d" k))
                )

                HoleDecl.Value(
                    sprintf "param-%d-label" k,
                    HoleValueSpace.StringLen(1, 40),
                    Some(Scalar.Str(sprintf "Parameter %d" k))
                )

                HoleDecl.Value(
                    sprintf "param-%d-control" k,
                    HoleValueSpace.Enum panelControlTokens,
                    Some(Scalar.Str "number")
                )

                HoleDecl.Value(
                    sprintf "param-%d-min" k,
                    HoleValueSpace.FloatRange(-1_000_000.0, 1_000_000.0),
                    Some(Scalar.Float 0.0)
                )

                HoleDecl.Value(
                    sprintf "param-%d-max" k,
                    HoleValueSpace.FloatRange(-1_000_000.0, 1_000_000.0),
                    Some(Scalar.Float 100.0)
                )

                HoleDecl.Value(
                    sprintf "param-%d-step" k,
                    HoleValueSpace.FloatRange(0.0001, 1_000_000.0),
                    Some(Scalar.Float 1.0)
                )

                HoleDecl.Value(
                    sprintf "param-%d-default" k,
                    HoleValueSpace.FloatRange(-1_000_000.0, 1_000_000.0),
                    Some(Scalar.Float 0.0)
                )

                HoleDecl.Value(sprintf "param-%d-options" k, HoleValueSpace.StringLen(1, 240), Some(Scalar.Str "=All")) ]

    /// One numeric parameter control: a `RangedNumber` chip writing filter key
    /// `name`, its bounds put in order and its starting value clamped into them.
    let private numberControl<'Msg>
        (name: string)
        (label: TextSource)
        (lo: float)
        (hi: float)
        (step: float)
        (start: float)
        : FilterSpec<'Msg> =
        let lo, hi = min lo hi, max lo hi

        { Name = name
          Label = label
          Kind =
            FormFieldKind.RangedNumber(
                Some(Binding.Filter(name, Some(JFloat(start |> max lo |> min hi)))),
                Option.None,
                Some lo,
                Some hi,
                Some step
            ) }

    let private panelTree<'Msg> (heading: TextSource) (controls: FilterSpec<'Msg> list) : Node<'Msg> =
        Fuaran.stack
            "parameter-panel"
            { Defaults.stack with
                Children =
                    [ Fuaran.heading
                          "parameter-panel-heading"
                          { Defaults.heading with
                              Level = 3
                              Text = heading
                              Variant = HeadingVariant.Eyebrow }
                      Fuaran.filters "parameter-panel-controls" controls ] }

    let private panelMaterialize<'Msg> : Map<string, obj> -> Node<'Msg> =
        fun b ->
            let control k =
                let hole (field: string) = sprintf "param-%d-%s" k field

                let name =
                    match readStr (sprintf "param-%d" k) (hole "name") b with
                    | n when System.String.IsNullOrWhiteSpace n -> sprintf "param-%d" k
                    | n -> n

                let label = TextSource.Literal(readStr (sprintf "Parameter %d" k) (hole "label") b)

                if readStr "number" (hole "control") b = "choice" then
                    choiceChip name label (Binding.Static(Some(parseFilterOptions (readStr "=All" (hole "options") b))))
                else
                    numberControl
                        name
                        label
                        (readNum 0.0 (hole "min") b)
                        (readNum 100.0 (hole "max") b)
                        (readNum 1.0 (hole "step") b)
                        (readNum 0.0 (hole "default") b)

            let controls =
                [ for k in 1 .. min (readCount 1 "count" b) PanelMaxParameters -> control k ]
                |> List.distinctBy (fun c -> c.Name)

            panelTree (TextSource.Literal(readStr "Parameters" "heading" b)) controls

    let parameterPanel<'Msg> : StdlibFragment<'Msg> =
        { Name = ParameterPanel
          Summary =
            "One to four controls, each writing the filter key a pipeline's parameter of the same name reads: a bounded number or a choice."
          Decl =
            decl
                ParameterPanel
                (panelTree
                    (holeText "heading" "Parameters")
                    [ numberControl "param-1" (holeText "param-1-label" "Parameter 1") 0.0 100.0 1.0 0.0 ])
                panelHoles
                // The controls WRITE host filter state — structure-only
                // certification, as for the other effecting fragments.
                writesHost
          Materialize = panelMaterialize
          Example =
            reference
                ParameterPanel
                [ "heading", FragmentArg.Str "Assumptions"
                  "count", FragmentArg.Int 1
                  "param-1-name", FragmentArg.Str "growth"
                  "param-1-label", FragmentArg.Str "Annual growth"
                  "param-1-min", FragmentArg.Float -0.1
                  "param-1-max", FragmentArg.Float 0.2
                  "param-1-step", FragmentArg.Float 0.01
                  "param-1-default", FragmentArg.Float 0.05 ] }

    // ════════════════════════════════════════════════════════════════════════
    //  10. scenario-comparison — one model, two parameter sets, the difference.
    // ════════════════════════════════════════════════════════════════════════

    [<Literal>]
    let ScenarioComparison = "scenario-comparison"

    /// The comparison's output columns. A source column of the same name is
    /// replaced by the derivation, so a model's rows should not carry one.
    [<Literal>]
    let ScenarioAColumn = "scenario-a"

    [<Literal>]
    let ScenarioBColumn = "scenario-b"

    [<Literal>]
    let DifferenceColumn = "difference"

    /// The name scenario `side` (`"a"` or `"b"`) reads model parameter `p` under
    /// in the comparison's pipeline — its `TransformParam` name.
    let scenarioParamName (side: string) (p: string) : string = side + "." + p

    /// The model's SCENARIO parameters: its function's parameters that no column
    /// feeds, in declaration order.
    let scenarioParameters (model: ScenarioModel) : string list =
        model.Model.Params
        |> List.map (fun p -> p.Name)
        |> List.filter (fun p -> not (List.exists (fun (q, _) -> q = p) model.Columns))

    /// The comparison's pipeline as DEFINED, and the whole of its arithmetic:
    /// define the model once, call it once per scenario (a row's columns for the
    /// column parameters, the scenario's own `Param`s for the rest), derive the
    /// difference `scenario-b - scenario-a`, and keep the key and the three
    /// figures. The fragment computes nothing; the pipeline does.
    let scenarioPipelineDefined (model: ScenarioModel) : Fuaran.Compute.Transform list =
        let call side =
            Fuaran.Compute.Call(
                model.Model.Name,
                model.Model.Params
                |> List.map (fun p ->
                    match List.tryFind (fun (q, _) -> q = p.Name) model.Columns with
                    | Some(_, column) -> Fuaran.Compute.Col column
                    | Option.None -> Fuaran.Compute.ColExpr.Param(scenarioParamName side p.Name))
            )

        [ Fuaran.Compute.Define model.Model
          Fuaran.Compute.Derive(ScenarioAColumn, call "a")
          Fuaran.Compute.Derive(ScenarioBColumn, call "b")
          Fuaran.Compute.Derive(
              DifferenceColumn,
              Fuaran.Compute.Binary(
                  Fuaran.Compute.Sub,
                  Fuaran.Compute.Col ScenarioBColumn,
                  Fuaran.Compute.Col ScenarioAColumn
              )
          )
          Fuaran.Compute.Project(
              [ model.Key; ScenarioAColumn; ScenarioBColumn; DifferenceColumn ]
              |> List.map (fun c -> c, c)
          ) ]

    /// The pipeline the comparison EMITS: `scenarioPipelineDefined` with each call
    /// replaced by the model's body (`FnDef.expand`, the expansion every
    /// evaluator performs first), so a host whose codec does not carry function
    /// definitions still reads it. The two scenario derivations are the one model
    /// over two parameter sets; the expansion only writes it out twice. Refused
    /// where the model is (a call the expansion refuses, a body over its bound).
    let scenarioPipeline (model: ScenarioModel) : Result<Fuaran.Compute.Transform list, string> =
        Fuaran.Compute.FnDef.expand (scenarioPipelineDefined model)
        |> Result.mapError (sprintf "the model '%s' does not expand: %A" model.Model.Name)

    /// The labels a comparison shows: its heading, then the key, scenario A,
    /// scenario B and difference column headings.
    type private ComparisonLabels =
        { Heading: TextSource
          Key: string
          A: string
          B: string
          Difference: string }

    let private figureColumn<'Msg> (label: string) (field: string) : ColumnErased<'Msg> =
        { Defaults.columnErased with
            Label = label
            Field = Some field
            Kind = CellKindErased.Numeric
            Format = CellFormat.Number(Some 2, None) }

    /// The comparison tree — total: it assumes `sides` binds every scenario
    /// parameter, which `scenarioComparisonOf` checks and the stdlib entry's
    /// holes guarantee.
    let private comparisonTree<'Msg>
        (id: string)
        (labels: ComparisonLabels)
        (model: ScenarioModel)
        (pipeline: Fuaran.Compute.Transform list)
        (a: ScenarioSide)
        (b: ScenarioSide)
        : Node<'Msg> =
        let parameters (side: string) (s: ScenarioSide) : TransformParam list =
            [ for p in scenarioParameters model do
                  match List.tryFind (fun (q, _) -> q = p) s.Parameters with
                  | Some(_, from) ->
                      ({ Name = scenarioParamName side p
                         From = from }
                      : TransformParam)
                  | Option.None -> () ]

        let ps = parameters "a" a @ parameters "b" b

        let grid: DataGridSpec<'Msg> =
            { Defaults.dataGrid with
                Source =
                    Binding.Transform(
                        TransformSource.Data model.Source,
                        pipeline,
                        (if List.isEmpty ps then Option.None else Some ps)
                    )
                RowKeyField = Some model.Key
                Columns =
                    [ { Defaults.columnErased with
                          Label = labels.Key
                          Field = Some model.Key }
                      figureColumn labels.A ScenarioAColumn
                      figureColumn labels.B ScenarioBColumn
                      figureColumn labels.Difference DifferenceColumn ] }

        Fuaran.stack
            id
            { Defaults.stack with
                Children =
                    [ Fuaran.heading
                          (id + "-heading")
                          { Defaults.heading with
                              Level = 3
                              Text = labels.Heading }
                      { Fuaran.stack (id + "-grid") Defaults.stack with
                          Kind = NodeKind.DataGrid grid
                          Accessibility = Defaults.Accessibility.grid } ] }

    /// A scenario comparison over a caller's own model, as a plain subtree rooted
    /// at `id` — the F# authoring form of the `scenario-comparison` fragment, for
    /// a model the stdlib entry does not carry. Refused when a side leaves a
    /// scenario parameter unbound, binds a name the model does not have, the
    /// model names a column parameter it does not declare, or its function does
    /// not expand.
    let scenarioComparisonOf<'Msg>
        (id: string)
        (heading: string)
        (model: ScenarioModel)
        (a: ScenarioSide)
        (b: ScenarioSide)
        : Result<Node<'Msg>, string> =
        let declared = model.Model.Params |> List.map (fun p -> p.Name)
        let wanted = scenarioParameters model

        let sideError (s: ScenarioSide) =
            let bound = s.Parameters |> List.map fst

            match
                List.tryFind (fun p -> not (List.contains p bound)) wanted,
                List.tryFind (fun p -> not (List.contains p wanted)) bound
            with
            | Some p, _ -> Some(sprintf "scenario '%s' leaves parameter '%s' unbound" s.Label p)
            | _, Some p ->
                Some(sprintf "scenario '%s' binds '%s', which is not a scenario parameter of the model" s.Label p)
            | Option.None, Option.None -> Option.None

        match
            model.Columns |> List.tryFind (fun (p, _) -> not (List.contains p declared)), sideError a, sideError b
        with
        | Some(p, _), _, _ -> Error(sprintf "the model has no parameter '%s' for a column to feed" p)
        | _, Some e, _
        | _, _, Some e -> Error e
        | Option.None, Option.None, Option.None ->
            scenarioPipeline model
            |> Result.map (fun pipeline ->
                comparisonTree
                    id
                    { Heading = TextSource.Literal heading
                      Key = model.Key
                      A = a.Label
                      B = b.Label
                      Difference = "Difference" }
                    model
                    pipeline
                    a
                    b)

    // The stdlib entry: the comparison over one general model a host can point at
    // its own rows — a measure projected under a growth rate,
    // `base * (1 + growth)` — with the rows named by the host (`source`), the key
    // and measure columns as holes, and each scenario's growth a bounded hole.

    let private growthModel (source: string) (key: string) (measure: string) : ScenarioModel =
        { Source = Fuaran.Core.DataSource.Ref source
          Key = key
          Model =
            { Name = "projection"
              Params =
                [ { Name = "base"; Default = Option.None }
                  { Name = "growth"
                    Default = Option.None } ]
              Body =
                Fuaran.Compute.Binary(
                    Fuaran.Compute.Mul,
                    Fuaran.Compute.Arg "base",
                    Fuaran.Compute.Binary(
                        Fuaran.Compute.Add,
                        Fuaran.Compute.ColExpr.Lit(Fuaran.Core.Float 1.0),
                        Fuaran.Compute.Arg "growth"
                    )
                ) }
          Columns = [ "base", measure ] }

    /// The growth model's comparison. Its expansion cannot be refused: the body is
    /// two arithmetic nodes and expansion does not read the column names, so the
    /// defined form below is a total function's unreachable arm, not a fallback
    /// any binding takes (the suite pins `scenarioPipeline` Ok for this model).
    let private growthComparison<'Msg>
        (labels: ComparisonLabels)
        (model: ScenarioModel)
        (a: ScenarioSide)
        (b: ScenarioSide)
        : Node<'Msg> =
        let pipeline =
            match scenarioPipeline model with
            | Ok pipeline -> pipeline
            | Error _ -> scenarioPipelineDefined model

        comparisonTree "scenario-comparison" labels model pipeline a b

    let private comparisonHoles: HoleDecl list =
        [ HoleDecl.Value("heading", HoleValueSpace.StringLen(1, 60), Some(Scalar.Str "Scenarios"))
          HoleDecl.Value("source", HoleValueSpace.StringLen(1, 64), Some(Scalar.Str "scenario-rows"))
          HoleDecl.Value("key", HoleValueSpace.StringLen(1, 32), Some(Scalar.Str "item"))
          HoleDecl.Value("measure", HoleValueSpace.StringLen(1, 32), Some(Scalar.Str "amount"))
          HoleDecl.Value("key-label", HoleValueSpace.StringLen(1, 40), Some(Scalar.Str "Item"))
          HoleDecl.Value("a-label", HoleValueSpace.StringLen(1, 40), Some(Scalar.Str "Scenario A"))
          HoleDecl.Value("b-label", HoleValueSpace.StringLen(1, 40), Some(Scalar.Str "Scenario B"))
          HoleDecl.Value("difference-label", HoleValueSpace.StringLen(1, 40), Some(Scalar.Str "Difference"))
          HoleDecl.Value("a-growth", HoleValueSpace.FloatRange(-1.0, 1.0), Some(Scalar.Float 0.0))
          HoleDecl.Value("b-growth", HoleValueSpace.FloatRange(-1.0, 1.0), Some(Scalar.Float 0.05)) ]

    /// A column-name hole's value, falling back to its default when blank.
    let private readName (fallback: string) (hole: string) (b: Map<string, obj>) : string =
        match readStr fallback hole b with
        | n when System.String.IsNullOrWhiteSpace n -> fallback
        | n -> n

    let private comparisonMaterialize<'Msg> : Map<string, obj> -> Node<'Msg> =
        fun b ->
            // The headings ride `ComparisonLabels`; a side here is its growth.
            let side (hole: string) (fallback: float) : ScenarioSide =
                { Label = hole
                  Parameters = [ "growth", Binding.Static(Some(JFloat(readNum fallback hole b))) ] }

            growthComparison
                { Heading = TextSource.Literal(readStr "Scenarios" "heading" b)
                  Key = readStr "Item" "key-label" b
                  A = readStr "Scenario A" "a-label" b
                  B = readStr "Scenario B" "b-label" b
                  Difference = readStr "Difference" "difference-label" b }
                (growthModel
                    (readName "scenario-rows" "source" b)
                    (readName "item" "key" b)
                    (readName "amount" "measure" b))
                (side "a-growth" 0.0)
                (side "b-growth" 0.05)

    let scenarioComparison<'Msg> : StdlibFragment<'Msg> =
        { Name = ScenarioComparison
          Summary =
            "One model run under two parameter sets, side by side with their difference — every figure computed by the pipeline."
          Decl =
            decl
                ScenarioComparison
                (growthComparison
                    { Heading = holeText "heading" "Scenarios"
                      Key = "Item"
                      A = "Scenario A"
                      B = "Scenario B"
                      Difference = "Difference" }
                    (growthModel "scenario-rows" "item" "amount")
                    { Label = "Scenario A"
                      Parameters = [ "growth", Binding.State("a-growth", Some(JFloat 0.0)) ] }
                    { Label = "Scenario B"
                      Parameters = [ "growth", Binding.State("b-growth", Some(JFloat 0.05)) ] })
                comparisonHoles
                // The rows are the host's (`source` names them).
                readsHost
          Materialize = comparisonMaterialize
          Example =
            reference
                ScenarioComparison
                [ "heading", FragmentArg.Str "Next year's budget"
                  "source", FragmentArg.Str "budget-lines"
                  "key", FragmentArg.Str "line"
                  "measure", FragmentArg.Str "cost"
                  "a-label", FragmentArg.Str "Flat"
                  "b-label", FragmentArg.Str "Inflation"
                  "a-growth", FragmentArg.Float 0.0
                  "b-growth", FragmentArg.Float 0.03 ] }

    // ════════════════════════════════════════════════════════════════════════
    //  11. refinable-table — a grid whose view is a pipeline's parameters.
    // ════════════════════════════════════════════════════════════════════════

    [<Literal>]
    let RefinableTableName = "refinable-table"

    // Phase 2753. A reader refines a table three ways: a membership filter, a
    // header sort and a page. All three are already state the tree names. A
    // filter is a chip's filter key, which the grid's pipeline reads as a list
    // parameter (`InParam`); the sort and the page are the grid's own
    // `sortStateKey` and `pageStateKey`, which the grid applies to the rows the
    // pipeline yields. The grid-behaviour rule (the state key is the affordance)
    // is followed as it stands: the sort and the page stay the grid's, and no
    // pipeline step reads them. A pipeline could not read the sort as it is
    // written anyway: the descriptor is an object naming a column INDEX, and a
    // `Sort` key's direction is a literal, never a slot.
    //
    // So the view is data: the three values the host holds, read as a
    // `RefinableView`. `refinedPipeline` writes that view as one pipeline, the
    // fixed pipeline with its parameters substituted and the sort and page as
    // literal `Sort` and `Limit` steps, which evaluates to the rows the grid
    // shows. That is the form a host stores, shares or re-applies.

    /// The State key a refinable table's grid writes its sort descriptor to.
    let refinableSortKey (id: string) : string = id + "-sort"

    /// The State key a refinable table's grid writes its page descriptor to.
    let refinablePageKey (id: string) : string = id + "-page"

    /// The filter key a refinable table's membership chip over `field` writes,
    /// and the name of the list parameter its pipeline reads it under.
    let refinableFilterKey (id: string) (field: string) : string = id + "-filter-" + field

    /// The column a text sort orders by in the substituted pipeline: the sorted
    /// column folded to lower case, which is the order the grid's header sort
    /// applies to text. The final projection drops it.
    let private sortKeyColumn (field: string) : string = "sort-key:" + field

    /// The table's FIXED pipeline: one membership `Filter` per filterable
    /// column, each reading the list parameter its chip writes. An unset or
    /// empty selection leaves its filter unbound, and the evaluator prunes it.
    let refinablePipeline (id: string) (table: RefinableTable) : Fuaran.Compute.Transform list =
        [ for c in table.Columns do
              if Option.isSome c.Filter then
                  Fuaran.Compute.Filter(
                      Fuaran.Compute.InParam(Fuaran.Compute.Col c.Field, refinableFilterKey id c.Field)
                  ) ]

    /// The current view as its canonical pipeline: `refinablePipeline` with each
    /// selected filter substituted as literals (`Transform.substituteListParams`,
    /// the substitution every evaluator performs) and each unselected filter
    /// pruned, then the sort as a literal `Sort`, the page as a literal `Limit`
    /// and a projection to the row key and the displayed columns.
    ///
    /// Pure, and it evaluates to the rows the grid shows for the same view. A
    /// sort descriptor naming no column of the table orders nothing, as the
    /// grid ignores it. A text column sorts by its lower-cased value, which is
    /// the grid's text order; ties keep their order on both sides, since both
    /// sorts are stable and put empty cells last in either direction.
    let refinedPipeline (id: string) (table: RefinableTable) (view: RefinableView) : Fuaran.Compute.Transform list =
        let selections =
            view.Filters
            |> List.filter (fun (_, values) -> not (List.isEmpty values))
            |> List.map (fun (field, values) -> refinableFilterKey id field, values |> List.map Fuaran.Core.Str)
            |> Map.ofList

        let filters =
            Fuaran.Compute.Transform.substituteListParams selections (refinablePipeline id table)
            |> List.filter (fun step ->
                match step with
                | Fuaran.Compute.Filter predicate -> List.isEmpty (Fuaran.Compute.ColExpr.paramsOf predicate)
                | _ -> true)

        let sort =
            match
                view.Sort
                |> Option.bind (fun (i, d) -> List.tryItem i table.Columns |> Option.map (fun c -> c, d))
            with
            | Option.None -> []
            | Some(c, direction) ->
                let dir =
                    match direction with
                    | Fuaran.UI.Types.SortDirection.Asc -> Fuaran.Compute.Asc
                    | Fuaran.UI.Types.SortDirection.Desc -> Fuaran.Compute.Desc

                match c.Kind with
                | RefinableKind.Number -> [ Fuaran.Compute.Sort [ Fuaran.Compute.Slot.Lit c.Field, dir ] ]
                | RefinableKind.Text ->
                    [ Fuaran.Compute.Derive(
                          sortKeyColumn c.Field,
                          Fuaran.Compute.ApplyFn(Fuaran.Compute.Lower, [ Fuaran.Compute.Col c.Field ])
                      )
                      Fuaran.Compute.Sort [ Fuaran.Compute.Slot.Lit(sortKeyColumn c.Field), dir ] ]

        let size = max 1 table.PageSize

        filters
        @ sort
        @ [ Fuaran.Compute.Limit(Fuaran.Compute.Slot.Lit size, Fuaran.Compute.Slot.Lit((max 1 view.Page - 1) * size))
            Fuaran.Compute.Project(
                table.Key :: (table.Columns |> List.map _.Field)
                |> List.distinct
                |> List.map (fun f -> f, f)
            ) ]

    let private refinableColumn<'Msg> (c: RefinableColumn) : ColumnErased<'Msg> =
        { Defaults.columnErased with
            Label = c.Label
            Field = Some c.Field
            Kind =
                (match c.Kind with
                 | RefinableKind.Number -> CellKindErased.Numeric
                 | RefinableKind.Text -> CellKindErased.Text)
            Sortable = (if c.Sortable then Option.None else Some false) }

    /// The table tree — total over any table: a heading, a filter bar holding
    /// one token chip per filterable column (omitted when none is), and the
    /// grid over the fixed pipeline. A filter with no suggestions takes free
    /// text.
    let private refinableTree<'Msg> (id: string) (heading: TextSource) (table: RefinableTable) : Node<'Msg> =
        let filterable =
            table.Columns
            |> List.choose (fun c -> c.Filter |> Option.map (fun options -> c, options))

        let chips: FilterSpec<'Msg> list =
            [ for c, options in filterable ->
                  { Name = refinableFilterKey id c.Field
                    Label = TextSource.Literal c.Label
                    Kind =
                      FilterField.tokens
                          (refinableFilterKey id c.Field)
                          (if List.isEmpty options then
                               Option.None
                           else
                               Some(Binding.Static(Some options)))
                          (List.isEmpty options) } ]

        let parameters: TransformParam list =
            [ for c, _ in filterable ->
                  { Name = refinableFilterKey id c.Field
                    From = Binding.Filter(refinableFilterKey id c.Field, Option.None) } ]

        let grid: DataGridSpec<'Msg> =
            { Defaults.dataGrid with
                Source =
                    Binding.Transform(
                        TransformSource.Data table.Source,
                        refinablePipeline id table,
                        (if List.isEmpty parameters then
                             Option.None
                         else
                             Some parameters)
                    )
                RowKeyField = Some table.Key
                SortStateKey = Some(refinableSortKey id)
                PageStateKey = Some(refinablePageKey id)
                PageSize = Some(max 1 table.PageSize)
                Columns = table.Columns |> List.map refinableColumn }

        Fuaran.stack
            id
            { Defaults.stack with
                Children =
                    [ Fuaran.heading
                          (id + "-heading")
                          { Defaults.heading with
                              Level = 3
                              Text = heading } ]
                    @ (if List.isEmpty chips then
                           []
                       else
                           [ Fuaran.filters (id + "-filters") chips ])
                    @ [ { Fuaran.stack (id + "-grid") Defaults.stack with
                            Kind = NodeKind.DataGrid grid
                            Accessibility = Defaults.Accessibility.grid } ] }

    /// A refinable table over a caller's own rows, as a plain subtree rooted at
    /// `id` — the F# authoring form of the `refinable-table` fragment. `sources`
    /// declares the schema of a `Ref` source (`SchemaWalk.noSources` declares
    /// none).
    ///
    /// Refused when the table has no columns, a page size below one, or two
    /// columns over one field; and, wherever the fixed pipeline's static schema
    /// is known, when it does not produce the row key or a sortable or
    /// filterable column, or produces a filterable column that is not text. A
    /// schema the walk cannot close (a `Ref` it was given no schema for) refutes
    /// nothing, and the grid's own grounding check takes over at pre-emit.
    let refinableTableOf<'Msg>
        (id: string)
        (heading: string)
        (sources: string -> Fuaran.Core.Schema option)
        (table: RefinableTable)
        : Result<Node<'Msg>, string> =
        let produced =
            Fuaran.Compute.SchemaWalk.ofPipelineFrom
                sources
                (Fuaran.Compute.SchemaWalk.ofSource sources table.Source)
                (refinablePipeline id table)

        let closed = Fuaran.Compute.SchemaWalk.isClosed produced

        let missing (field: string) =
            closed && not (Fuaran.Compute.SchemaWalk.has field produced)

        let fields = table.Columns |> List.map _.Field

        let firstColumn (p: RefinableColumn -> bool) = table.Columns |> List.tryFind p

        if List.isEmpty table.Columns then
            Error "a refinable table needs at least one column"
        elif table.PageSize < 1 then
            Error(sprintf "the page size is %d; a page holds at least one row" table.PageSize)
        elif List.length (List.distinct fields) <> List.length fields then
            Error "two columns display one field; each field is one column"
        elif missing table.Key then
            Error(sprintf "the pipeline's static schema does not produce the row key '%s'" table.Key)
        else
            match
                firstColumn (fun c -> c.Sortable && missing c.Field),
                firstColumn (fun c -> Option.isSome c.Filter && missing c.Field),
                firstColumn (fun c ->
                    Option.isSome c.Filter
                    && (match Fuaran.Compute.SchemaWalk.typeOf c.Field produced with
                        | Some t -> t <> Fuaran.Core.StringType
                        | Option.None -> false))
            with
            | Some c, _, _ ->
                Error(sprintf "the sortable column '%s' is not a column the pipeline's static schema produces" c.Field)
            | _, Some c, _ ->
                Error(
                    sprintf "the filterable column '%s' is not a column the pipeline's static schema produces" c.Field
                )
            | _, _, Some c ->
                Error(
                    sprintf "the filterable column '%s' is not text; a membership filter compares text values" c.Field
                )
            | Option.None, Option.None, Option.None -> Ok(refinableTree id (TextSource.Literal heading) table)

    // The stdlib entry: a table over host-named rows (`source`, a `Ref`), with
    // one to six columns, each a bounded set of holes, and the page size a hole.

    [<Literal>]
    let private RefinableMaxColumns = 6

    let private refinableHoles: HoleDecl list =
        [ HoleDecl.Value("heading", HoleValueSpace.StringLen(1, 60), Some(Scalar.Str "Records"))
          HoleDecl.Value("source", HoleValueSpace.StringLen(1, 64), Some(Scalar.Str "table-rows"))
          HoleDecl.Value("key", HoleValueSpace.StringLen(1, 32), Some(Scalar.Str "id"))
          HoleDecl.Value("page-size", HoleValueSpace.IntRange(1, 100), Some(Scalar.Int 10))
          HoleDecl.Repeat("count", HoleValueSpace.IntRange(1, RefinableMaxColumns)) ]
        @ [ for k in 1..RefinableMaxColumns do
                HoleDecl.Value(
                    sprintf "column-%d-field" k,
                    HoleValueSpace.StringLen(1, 32),
                    Some(Scalar.Str(sprintf "column-%d" k))
                )

                HoleDecl.Value(
                    sprintf "column-%d-label" k,
                    HoleValueSpace.StringLen(1, 40),
                    Some(Scalar.Str(sprintf "Column %d" k))
                )

                HoleDecl.Value(
                    sprintf "column-%d-kind" k,
                    HoleValueSpace.Enum [ "text"; "number" ],
                    Some(Scalar.Str "text")
                )

                HoleDecl.Value(
                    sprintf "column-%d-sort" k,
                    HoleValueSpace.Enum [ "sortable"; "fixed" ],
                    Some(Scalar.Str "sortable")
                )

                HoleDecl.Value(
                    sprintf "column-%d-filter" k,
                    HoleValueSpace.Enum [ "none"; "membership" ],
                    Some(Scalar.Str "none")
                )

                HoleDecl.Value(sprintf "column-%d-options" k, HoleValueSpace.StringLen(1, 240), Some(Scalar.Str "=All")) ]

    /// A filter's suggestions from the filter-bar's options grammar. A
    /// membership selection is a set of values, so an option with the empty
    /// value (the grammar's "All") is no suggestion and is dropped.
    let private membershipOptions (raw: string) : SelectOption list =
        parseFilterOptions raw |> List.filter (fun o -> o.Value <> "")

    let private refinableMaterialize<'Msg> : Map<string, obj> -> Node<'Msg> =
        fun b ->
            let column k : RefinableColumn =
                let hole (part: string) = sprintf "column-%d-%s" k part

                { Field = readName (sprintf "column-%d" k) (hole "field") b
                  Label = readStr (sprintf "Column %d" k) (hole "label") b
                  Kind =
                    (if readStr "text" (hole "kind") b = "number" then
                         RefinableKind.Number
                     else
                         RefinableKind.Text)
                  Sortable = readStr "sortable" (hole "sort") b <> "fixed"
                  Filter =
                    (if readStr "none" (hole "filter") b = "membership" then
                         Some(membershipOptions (readStr "=All" (hole "options") b))
                     else
                         Option.None) }

            refinableTree
                RefinableTableName
                (TextSource.Literal(readStr "Records" "heading" b))
                { Source = Fuaran.Core.DataSource.Ref(readName "table-rows" "source" b)
                  Key = readName "id" "key" b
                  // A field bound twice keeps its first column, so no filter key
                  // has two writers and no field two columns.
                  Columns =
                    [ for k in 1 .. min (readCount 1 "count" b) RefinableMaxColumns -> column k ]
                    |> List.distinctBy _.Field
                  PageSize = readCount 10 "page-size" b }

    let refinableTable<'Msg> : StdlibFragment<'Msg> =
        { Name = RefinableTableName
          Summary =
            "A grid whose sort, page and membership filters are its view: state the grid and its chips write, which the substituted pipeline restates."
          Decl =
            decl
                RefinableTableName
                (refinableTree
                    RefinableTableName
                    (holeText "heading" "Records")
                    { Source = Fuaran.Core.DataSource.Ref "table-rows"
                      Key = "id"
                      Columns =
                        [ { Field = "column-1"
                            Label = "Column 1"
                            Kind = RefinableKind.Text
                            Sortable = true
                            Filter = Option.None } ]
                      PageSize = 10 })
                refinableHoles
                // The grid writes its sort and page state and the chips write
                // filter state, over rows the host resolves.
                writesHost
          Materialize = refinableMaterialize
          Example =
            reference
                RefinableTableName
                [ "heading", FragmentArg.Str "Open orders"
                  "source", FragmentArg.Str "orders"
                  "key", FragmentArg.Str "order"
                  "page-size", FragmentArg.Int 25
                  "count", FragmentArg.Int 3
                  "column-1-field", FragmentArg.Str "customer"
                  "column-1-label", FragmentArg.Str "Customer"
                  "column-2-field", FragmentArg.Str "region"
                  "column-2-label", FragmentArg.Str "Region"
                  "column-2-filter", FragmentArg.Str "membership"
                  "column-2-options", FragmentArg.Str "north=North|south=South"
                  "column-3-field", FragmentArg.Str "total"
                  "column-3-label", FragmentArg.Str "Total"
                  "column-3-kind", FragmentArg.Str "number" ] }

    // ════════════════════════════════════════════════════════════════════════
    //  The library.
    // ════════════════════════════════════════════════════════════════════════

    /// The curated set, in the order the README introduces them. Every entry is
    /// certified in the library's own suite; nothing lands here uncertified.
    let all<'Msg> : StdlibFragment<'Msg> list =
        [ labelledMetricRow
          kpiStrip
          filterBar
          confirmActionPair
          emptyStatePanel
          sectionHeader
          metricCard
          loadingPlaceholder
          parameterPanel
          scenarioComparison
          refinableTable ]

    /// Further representative applications, each binding holes a fragment's
    /// `Example` leaves at their defaults: `(fragment name, what it shows, the
    /// reference)`. Phase 2471 — the kpi-strip's per-figure format, trend and
    /// polarity holes, and the filter-bar's choice filters; Phase 2752 — the
    /// parameter-panel's choice control. The `Example` of each
    /// stays exactly as it was, so these sit beside it rather than replacing it,
    /// and the corpus pins both.
    let boundExamples<'Msg> : (string * string * Node<'Msg>) list =
        [ KpiStrip,
          "per-figure format, trend and polarity bound",
          apply
              (refIdOf KpiStrip + "-figures")
              KpiStrip
              [ "heading", FragmentArg.Str "This week"
                "count", FragmentArg.Int 3
                "currency", FragmentArg.Str "EUR"
                "format-1", FragmentArg.Str "currency"
                "trend-1", FragmentArg.Str "percent-1"
                "format-2", FragmentArg.Str "number-0"
                "trend-2", FragmentArg.Str "value"
                "format-3", FragmentArg.Str "percent-1"
                "trend-3", FragmentArg.Str "percent-1"
                "polarity-3", FragmentArg.Str "lower-is-better" ]
          FilterBar,
          "two choice filters, search chip hidden",
          apply
              (refIdOf FilterBar + "-choices")
              FilterBar
              [ "search", FragmentArg.Str "hidden"
                "choices", FragmentArg.Int 2
                "choice-1-label", FragmentArg.Str "Region"
                "choice-1-key", FragmentArg.Str "region"
                "choice-1-options", FragmentArg.Str "=All regions|north=North|south=South"
                "choice-2-label", FragmentArg.Str "Comparison"
                "choice-2-key", FragmentArg.Str "compare"
                "choice-2-options", FragmentArg.Str "lw=vs last week|ly=vs last year" ]
          // Phase 2752 — a panel mixing both control kinds.
          ParameterPanel,
          "a number and a choice parameter",
          apply
              (refIdOf ParameterPanel + "-mixed")
              ParameterPanel
              [ "count", FragmentArg.Int 2
                "param-1-name", FragmentArg.Str "growth"
                "param-1-label", FragmentArg.Str "Annual growth"
                "param-1-min", FragmentArg.Float -0.1
                "param-1-max", FragmentArg.Float 0.2
                "param-1-step", FragmentArg.Float 0.01
                "param-1-default", FragmentArg.Float 0.05
                "param-2-name", FragmentArg.Str "region"
                "param-2-label", FragmentArg.Str "Region"
                "param-2-control", FragmentArg.Str "choice"
                "param-2-options", FragmentArg.Str "=All regions|north=North|south=South" ] ]

    /// Look a fragment up by its stable name.
    let tryFind<'Msg> (name: string) : StdlibFragment<'Msg> option =
        all |> List.tryFind (fun f -> f.Name = name)

    /// Every fragment's derived signature — holes with their value-spaces and
    /// optionality, plus the declared effect class. The introspection surface an
    /// authoring tool or a pattern bank reads.
    let signatures<'Msg> () : FragmentSignature list =
        all<'Msg>
        |> List.map (fun f ->
            match f.Decl.Kind with
            | NodeKind.FragmentDecl spec -> Fragment.signature spec
            | _ ->
                // Unreachable by construction — `decl` only ever builds a
                // FragmentDecl — but a total match beats an exception.
                { Name = f.Name
                  Holes = []
                  Effect = EffectClass.pureDeterministic })
