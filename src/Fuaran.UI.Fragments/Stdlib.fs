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
//  a loading placeholder. Each declares TYPED HOLES over bounded value-spaces
//  and a two-axis EFFECT CLASS, and each is certified valid-for-all-bindings by
//  the verification floor in `Fuaran.UI.Validator.RecipeCertification`.
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
              Holes = (if List.isEmpty holes then Option.None else Some holes)
              Effect =
                (if
                     effect.HostEffect = HostEffect.Pure
                     && effect.Determinism = DeterminismSource.Deterministic
                 then
                     Option.None
                 else
                     Some effect) }

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
                Format = CellFormat.Number(Some 0) }

    let private labelledMetricRowMaterialize<'Msg> : Map<string, obj> -> Node<'Msg> =
        fun b ->
            Fuaran.labelValueRow
                "labelled-metric-row-row"
                { Defaults.labelValueRow with
                    Label = TextSource.Literal(readStr "Metric" "label" b)
                    Value = Binding.Static(Some(readNum 0.0 "value" b))
                    Format = CellFormat.Number(Some 0) }

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

    let private kpiStripHoles: HoleDecl list =
        [ HoleDecl.Value("heading", HoleValueSpace.StringLen(1, 40), Some(Scalar.Str "Key figures"))
          // TOTALITY (invariant 1): the count is a bounded `IntRange`, so no
          // binding can produce unbounded expansion.
          HoleDecl.Repeat("count", HoleValueSpace.IntRange(1, 6)) ]

    let private kpiMetric<'Msg> (i: int) : Node<'Msg> =
        Fuaran.metric
            (sprintf "kpi-strip-metric-%d" i)
            { Defaults.metric with
                Label = TextSource.Bound(Binding.State(sprintf "kpi-%d-label" i, Some(sprintf "KPI %d" i)))
                Value = Binding.State(sprintf "kpi-%d-value" i, Some 0.0)
                Format = CellFormat.Number(Some 0) }

    let private kpiStripTree<'Msg> (heading: string) (count: int) : Node<'Msg> =
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
                    :: [ for i in 1..count -> kpiMetric i ] }

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
                      kpiMetric 1 ] }

    let kpiStrip<'Msg> : StdlibFragment<'Msg> =
        { Name = KpiStrip
          Summary = "A headed horizontal strip of one to six figures — the dashboard's top band."
          Decl = decl KpiStrip kpiStripBody kpiStripHoles pureDeterministic
          Materialize = fun b -> kpiStripTree (readStr "Key figures" "heading" b) (readCount 1 "count" b)
          Example = reference KpiStrip [ "heading", FragmentArg.Str "This week"; "count", FragmentArg.Int 3 ] }

    // ════════════════════════════════════════════════════════════════════════
    //  3. filter-bar — a search chip and a status chip over host filter state.
    // ════════════════════════════════════════════════════════════════════════

    [<Literal>]
    let FilterBar = "filter-bar"

    let private filterBarHoles: HoleDecl list =
        [ HoleDecl.Value("searchLabel", HoleValueSpace.StringLen(1, 32), Some(Scalar.Str "Search"))
          HoleDecl.Value("statusLabel", HoleValueSpace.StringLen(1, 32), Some(Scalar.Str "Status")) ]

    let private statusOptions: Binding<SelectOption list> =
        Binding.Static(Some [ { Label = "Open"; Value = "open" }; { Label = "Closed"; Value = "closed" } ])

    let private filterBarTree<'Msg> (searchLabel: TextSource) (statusLabel: TextSource) : Node<'Msg> =
        Fuaran.filters
            "filter-bar"
            [ { Name = "search"
                Label = searchLabel
                Kind = FilterField.text "search" }
              { Name = "status"
                Label = statusLabel
                Kind = FilterField.choice "status" statusOptions } ]

    let filterBar<'Msg> : StdlibFragment<'Msg> =
        { Name = FilterBar
          Summary = "A free-text chip and a status chip, both bound to their own host filter keys."
          Decl =
            decl
                FilterBar
                (filterBarTree (holeText "searchLabel" "Search") (holeText "statusLabel" "Status"))
                filterBarHoles
                // The chips READ host filter state. Certification of an
                // effecting fragment asserts STRUCTURE only (Phase 52) — never
                // that the emission is a pure function of its holes.
                readsHost
          Materialize =
            fun b ->
                filterBarTree
                    (TextSource.Literal(readStr "Search" "searchLabel" b))
                    (TextSource.Literal(readStr "Status" "statusLabel" b))
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
                      Fuaran.markdownSpec "empty-state-panel-body" { Text = body }
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
                              Format = CellFormat.Number(Some 0) }
                      Fuaran.markdownSpec "metric-card-caption" { Text = caption } ] }

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
          loadingPlaceholder ]

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
