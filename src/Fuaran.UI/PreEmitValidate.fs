module Fuaran.UI.PreEmitValidate

// ============================================================================
//  Pre-emit tree-invariant checks for AI authors + human developers.
//
//  Fuaran's type system enforces *node-level* invariants (every Node has
//  required State + Style; every spec record's fields are typed). Two
//  invariants live above the type level — tree-wide identity uniqueness +
//  non-emptiness of identifier strings — and must be checked by walking
//  the tree. This module is the canonical walker.
//
//  Designed for both consumer paths:
//
//   1. **AI author pre-emit self-check** — before submitting a constructed
//      tree to the wire, the AI agent (or a planner harness) calls
//      `PreEmitValidate.validate` and fixes whatever the result reports.
//      Catches duplicate-NodeId collisions and empty identifier strings
//      cheaper than the wire-side `Apply` engine's `DuplicateNodeId` /
//      `PathInvalid` envelopes.
//
//   2. **Human-author test gate** — Expecto suites covering author-side
//      construction code call `validate` to assert the seed tree is
//      well-formed before exercising the renderer. Catches authoring
//      typos that the §4b type contract can't.
//
//  Fable-compatible — no reflection, no `obj` peek-through, no
//  `System.*` server-only API. Walks `Node<'Msg>.Kind` via direct
//  pattern match.
//
//  See `docs/AI_AUTHORING_GUIDE.md` § "Self-checking before you emit"
//  and `docs/ERROR_CODES.md` for the AI-side recovery patterns.
//
//  JSON-encoder counterpart (`JsonEncode.node : Node<'Msg> -> string`)
//  lives with op-stream persistence — it carries the same canonical-JSON
//  algorithm `OpStream` uses for hash chaining, so factoring it out keeps
//  the encoder definitions centralised. This module is the tree-shape half;
//  the wire-shape half ships with op-stream persistence.
// ============================================================================

open Fuaran.Core
open Fuaran.UI.Types
open Fuaran.UI.KindPolicy

/// A pre-emit defect surfaced by `validate`. Stable, AI-friendly
/// discriminator (camelCase rendering at the docs boundary).
[<RequireQualifiedAccess>]
type PreEmitDefect =
    /// `id` appears as the `NodeId` of multiple nodes in the tree. Carries
    /// the offending id string and the observed count (≥ 2). The §4g op
    /// vocabulary depends on `NodeId` uniqueness — duplicate ids make
    /// `EditNode`, `RemoveNode`, `UpdateProp` ambiguous.
    | DuplicateNodeId of id: string * count: int
    /// A `NodeId` is `""` (empty string). The `NodeId` newtype permits
    /// the empty string at the F# type level, but the wire form requires
    /// a non-empty identifier so the orchestrator can address the node
    /// in subsequent turns.
    | EmptyNodeId
    /// A `Custom` node has an empty `moduleId` or `componentId` string.
    /// The wire-form `kind: "Custom"` envelope needs both to dispatch.
    | EmptyCustomKindIdentifier of moduleId: string * componentId: string
    /// **FUARAN104 (Warning)**. A node's kind is outside the admitted set of the
    /// policy this walk was given (WIRE_FORMAT §23). Carries the node id, the
    /// node's WIRE discriminator, and the policy identity. Phase 1020.
    ///
    /// **Advisory, and the severity is the point.** The decode boundary is where
    /// a policy is ENFORCED; this is the authoring end telling an author that a
    /// tree they are about to emit will be refused by the deployment they named.
    /// It is an Error nowhere, because an authoring host may legitimately
    /// construct a tree for a *different* deployment under a *different* policy,
    /// and a walk cannot know which. Reported per offending node rather than
    /// once per tree: the author repairs each of them.
    | KindNotAdmitted of nodeId: string * kind: string * policy: string
    /// **FUARAN047 (Error)**. A `TabsSpec` carries
    /// `TabHeaders = Some hs` whose length does not equal `Children.Length`.
    /// The renderer aligns headers 1:1 with children by index; mismatched
    /// lengths leave tabs without labels or labels without targets. Carries
    /// the offending `NodeId`, the header count, and the children count.
    | TabHeaderCountMismatch of nodeId: string * headerCount: int * childrenCount: int
    /// **FUARAN048 (Error)**. A `TabsSpec` carries
    /// `TabTags = Some ts` whose length does not equal `Children.Length`.
    /// The typed tag overlay maps tags to children by index; mismatched
    /// lengths break the tag → index round-trip.
    | TabTagCountMismatch of nodeId: string * tagCount: int * childrenCount: int
    /// **FUARAN049 (Warning)**. A `TabsSpec` carries
    /// `ActiveTag = Some _` but `TabTags = None`. The tag binding has
    /// nothing to resolve against; the renderer silently falls back to
    /// `ActiveIndex`, which is rarely what the author intended. Carries
    /// the offending `NodeId`.
    | TabActiveTagWithoutTags of nodeId: string
    /// **FUARAN070 (Error)**. A `Binding.Selection` names a `NodeId` absent
    /// from the tree — the dangling-edge check `Selection` never had (the
    /// filter-wiring record's symmetry gap, closed by Phase 427). The reader
    /// can never resolve: no node exists to produce (or be host-fed) a
    /// selection under that id. Carries the reading node's id and the missing
    /// target id. Fix: point the binding at the selection-producing node's id.
    | DanglingSelection of readerNodeId: string * target: string
    /// **FUARAN071 (Warning)**. A `Binding.Selection` targets a node that
    /// exists but is not a selection-PRODUCING kind (a `Visualisation` — the
    /// grid's Phase 427 default row-click write; charts/tables/maps via host
    /// closures). A host can still populate `BindingSources.Selections` for
    /// any id, but nothing in the tree will ever write it — usually a
    /// mis-targeted id. Carries the reading node's id and the target id.
    | SelectionOverNonProducer of readerNodeId: string * target: string
    /// **FUARAN074 (Warning)**. A declared filter chip (`FilterSpec.Name` on a
    /// `Filters` node) is consumed by NOTHING — no `Binding.Filter` read
    /// outside the declaring chip, no `Query.dependsOn` reference, no
    /// `Transform` param source (the 421/424 consumption union). A decorative
    /// filter: the user can set it and nothing changes. Carries the declaring
    /// node's id and the filter name. (A chip's own `Binding.Filter` self-read
    /// — the 423 declarative-chip shape — deliberately does NOT count.)
    | DecorativeFilter of declaringNodeId: string * name: string
    /// **FUARAN075 (Error)**. A DECLARED filter→consumer edge — a
    /// `Query.dependsOn` name, or a `Transform` param whose source is
    /// `Binding.Filter` — references a filter no `Filters` chip declares
    /// (Phases 421/424). The edge can never fire from the tree; usually a
    /// name typo. (A plain `Binding.Filter` VALUE read is exempt — a host may
    /// legitimately feed `BindingSources.Filters` without chips.) Carries the
    /// reading node's id and the undeclared filter name.
    | DanglingFilterReference of readerNodeId: string * name: string
    /// **FUARAN076 (Warning)**. A `Binding.Transform` declares a `params`
    /// entry whose name the pipeline never references (`Transform.paramsOf`) —
    /// dead weight, usually a rename that missed the pipeline or vice versa.
    /// Carries the reading node's id and the param name.
    | UnreferencedTransformParam of readerNodeId: string * name: string
    /// **FUARAN077 (Warning)**. A grid column carries NEITHER a `Value`
    /// closure NOR a declarative `Field` (Phase 425) — the column renders
    /// blank in every host. Always give a decoded column a `field`. Carries
    /// the grid node's id and the column label.
    | BlankGridColumn of nodeId: string * columnLabel: string
    /// **FUARAN078 (Warning)**. A `DataGrid` carries NEITHER a `RowKey`
    /// closure NOR a declarative `RowKeyField` (Phase 425) — no stable row
    /// identity, so the Phase 427 selected-row state and any keyed diffing
    /// degrade. Carries the grid node's id.
    | UnstableRowIdentity of nodeId: string
    /// **FUARAN072 (Warning)**. An `Action.Call` carries `into: Query <name>`
    /// but no `Binding.Query <name>` anywhere in the tree reads that slot —
    /// an orphan fetch: the response lands where nothing looks (Phase 428).
    /// Usually a name typo; point the target at the query name a reader
    /// binds, or bind a reader. Carries the calling node's id and the name.
    | OrphanQueryFetch of readerNodeId: string * queryName: string
    /// **FUARAN073 (Warning)**. An `Action.Call` has neither an `onResult`
    /// closure nor an `into` target — the response is dropped (Phase 428).
    /// Legitimate for command-style endpoints (fire-and-forget), hence a
    /// warning; add `into` when the response carries data a reader needs.
    /// Carries the calling node's id and the endpoint.
    | CallResultDropped of readerNodeId: string * endpoint: string
    /// **FUARAN069 (Warning)**. An interactive control's event handler is
    /// omitted (`None` — the declarative / Phase 426 write-back shape) but its
    /// value binding is **not** a writable store binding (directly
    /// `Binding.State` or `Binding.Filter`), so the control is inert: no
    /// closure dispatches and the renderer has no slot to write the change
    /// back to. Carries the owning `NodeId` and a short control descriptor
    /// (e.g. `"FormField(profile-name)"`, `"Select"`, `"Tabs.ActiveIndex"`).
    /// Fix: bind the control's value to `$state.<key>` / `$filters.<name>`,
    /// or supply the handler.
    | InertControl of nodeId: string * control: string
    /// **FUARAN085 (Warning)**. Two handler-free form fields write back to the
    /// SAME `$state` key (Phase 596 — with the symmetric auto-bind, an omitted
    /// `value` binds `$state.<field id>`, so duplicated field ids across forms,
    /// or an explicit `State` key reusing another field's id, silently alias:
    /// typing in one field overwrites the other's captured value). READERS of
    /// the key are the feature and are not flagged — only two WRITERS collide.
    /// Carries the state key and the colliding (nodeId, fieldId) pairs.
    | DuplicateWriteBackKey of stateKey: string * writers: (string * string) list
    /// **FUARAN068 (Error)**. A REGISTERED custom component's prop bag
    /// violates its declared `PropSchema` — a required prop is missing, or a
    /// present prop's shape doesn't match its declared `PropType`. Carries
    /// the offending `NodeId`, the component identity, and the per-prop
    /// defect list from `CustomRegistry.ValidateProps`. Surfaced only by
    /// `validateWithRegistry` (the plain `validate` has no registry to check
    /// against — an unregistered custom kind is a host trust-boundary
    /// concern, not a schema violation).
    | CustomPropSchemaViolation of
        nodeId: string *
        moduleId: string *
        componentId: string *
        propDefects: CustomPropDefect list
    /// **FUARAN082 (Error)**. A `NodeKind.Switch` carries two or more cases with
    /// the same `match` value (Phase 392). First-match-wins makes the later case
    /// dead — it can never render, so it is almost always an authoring mistake
    /// (a copy-paste that forgot to change the match). Carries the switch node's
    /// id and the duplicated match value. (Missing `default` / `stateKey` /
    /// `cases` are decode-time `MISSING_FIELD` rejects — a required field is
    /// always present on an in-memory tree, so there is no pre-emit advisory for
    /// them.)
    | DuplicateSwitchMatch of nodeId: string * matchValue: string
    /// **FUARAN083 (Warning)**. A `NodeKind.Switch` carries an empty `stateKey`
    /// (Phase 392) — the ungrounded-state-key defect. A switch reads its state
    /// key to select a case; an empty key can never resolve, so the switch is
    /// stuck on its `default` forever. The default-deny posture (Phase 12.Y):
    /// a state consumer must name a key. (The policy-manifest grounding of a key
    /// against an *allowed* set is the orchestration tier's concern, where that
    /// manifest lives; the language tier grounds by shape — a named key.) Carries
    /// the switch node's id.
    | UngroundedSwitchStateKey of nodeId: string
    /// **FUARAN086 (Error)**. A `ChartSpec` field reference (`XField` or a
    /// `YFields` entry) names a column absent from the chart's statically-known
    /// data schema (Phase 640). Today the schema is statically known for a
    /// `Binding.Transform` over an `Embedded` table with an EMPTY pipeline —
    /// the common inline-data chart. A non-empty pipeline (Derive/Project/
    /// GroupBy change the column set), a `Ref` source, a `Query`, or a host
    /// `Static` obj-seq is unknowable pre-emit and deliberately passes
    /// ungrounded (the fuaran-core#90 rule: only refuse what is PROVABLY
    /// wrong). Carries the chart node's id and the ungrounded field name.
    | ChartFieldUngrounded of nodeId: string * field: string
    /// **FUARAN087 (Error)**. A grounded chart value field (a `YFields` entry,
    /// or `XField` on a `Scatter`) carries a column type the lowering cannot
    /// plot numerically — anything outside int/float/bool (bool coerces 1/0)
    /// reads 0.0 at lowering time, a silently-flat series (Phase 640). Fires
    /// only when the schema is statically known (see FUARAN086). Carries the
    /// node id, the field, and the offending column-type tag.
    | ChartFieldTypeMismatch of nodeId: string * field: string * columnType: string
    /// **FUARAN088 (Error)**. A `Pie` chart carries other than exactly ONE
    /// `YFields` series (Phase 640/638). The pie lowering REFUSES multi-series
    /// geometry rather than silently truncating to the first series, so a
    /// multi-series (or zero-series) pie renders nothing — always an authoring
    /// mistake: plot one share column, or switch kind. Carries the node id and
    /// the series count.
    | ChartPieSeriesShape of nodeId: string * seriesCount: int
    /// **FUARAN089 (Warning)**. `Stacked = true` on a `ChartKind` where
    /// stacking is meaningless (`Line` / `Scatter` / `Pie`) — the lowering
    /// ignores the flag (Phase 637), so the emission carries dead intent;
    /// usually a kind switch that forgot the flag. Carries the node id and the
    /// kind name.
    | ChartStackedMeaningless of nodeId: string * kind: string
    /// **FUARAN090 (Warning)**. A `DataGrid` carries `editable: true` but its
    /// `source` is not a direct `Binding.State` (Phase 663) — the FUARAN069
    /// inert-control condition replayed for the grid: the renderer has no
    /// writable slot to commit an edit to (a `Transform` pipeline is not
    /// invertible; `Static` / `Query` rows are host data; `staticRows` are
    /// immutable by definition), so every cell renders read-only and the flag
    /// is dead intent. Fix: source the grid — and every reader that should
    /// track edits, e.g. a chart over the same data — from a shared
    /// `{"$type": "State", "key": …, "default": [rows]}` binding. Carries the
    /// grid node's id.
    | InertEditableGrid of nodeId: string
    /// **FUARAN091 (Error)**. The tree nests nodes deeper than
    /// `WireLimits.MaxDepth` (Phase 781; WIRE_FORMAT §21). Reported ONCE, at the
    /// first node past the limit, carrying that node's id and the limit — the
    /// walk stops descending there, so a single over-deep subtree does not bury
    /// the rest of the report under thousands of identical entries.
    ///
    /// The point of the defect is what it replaces: before the bound, this walk
    /// simply recursed until the process died of a `StackOverflowException`,
    /// which .NET cannot catch, so no defect list of any kind came back.
    | MaxDepthExceeded of nodeId: string * limit: int
    /// **FUARAN092 (Warning)**. A `Link` declares `protection: "email"` on an
    /// href that is statically known NOT to be a `mailto:` (Phase 812). The
    /// Email protection strategy only has meaning over a mailto address — on
    /// anything else the renderers fall through to the ordinary anchor, so the
    /// flag is dead intent. Only statically-decidable hrefs are flagged
    /// (`Binding.Static`); a bound href that resolves at runtime is left
    /// alone. Carries the link node's id.
    | ProtectedNonMailtoLink of nodeId: string
    /// **FUARAN093 (Error)**. A `DataGrid` declares `pageSize` without a
    /// `pageStateKey` (Phase 862). A page size names how large a page is; with
    /// no key carrying the page POSITION there is no page to be on, so the grid
    /// renders every row and the declaration is inert. This is the residual
    /// authored form of the fake-affordance class the grid-behaviour charter
    /// names — the class is otherwise structural (the pager is renderer-owned,
    /// so it cannot be wired to nothing). Fix: add `pageStateKey`. Carries the
    /// grid node's id.
    | PageSizeWithoutPageKey of nodeId: string
    /// **FUARAN096 (Warning)**. A `DataGrid` declares `pageStateKey` +
    /// `pageSize` AND sources itself from a `Query` whose `dependsOn` names
    /// that same key (Phase 862). The host already returns the page, so a
    /// client-side slice on top would page the page — showing `pageSize` rows
    /// of an already-`pageSize`-row result and losing the rest. The renderer
    /// resolves this correctly (source shape decides who slices), so this is a
    /// caution rather than a break: it is reported because the shape is almost
    /// always a mis-wiring, and because the author cannot see which of the two
    /// mechanisms won by reading the tree. Carries the grid node's id and the
    /// key.
    | DoublePagedGrid of nodeId: string * pageStateKey: string
    /// **FUARAN094 (Error)**. A bound grid's sort declaration cannot be
    /// honoured (Phase 861). Three shapes, one code, because they are one
    /// defect from the author's side — a sort that will not happen:
    ///
    ///  - a column declares `sortable: true` under a grid naming no
    ///    `sortStateKey`, i.e. it tries to WIDEN a behaviour the grid never
    ///    turned on (the charter's narrowing rule refuses this direction);
    ///  - a column declares `sortable: true` with no `field`, so nothing
    ///    identifies the row property to order by;
    ///  - `defaultSort` names a column index outside the column set, or one
    ///    the grid cannot sort.
    ///
    /// Carries the grid node's id and a typed reason, so the message names the
    /// specific shape rather than the family.
    | UnhonourableSort of nodeId: string * reason: SortDefect
    /// **FUARAN095 (Error)**. A column is declared editable but has no
    /// reachable destination (Phase 863) — the decoded-and-inert shape census
    /// row #27 describes. Two shapes:
    ///
    ///  - a column declares `editable: true` under a grid whose own `editable`
    ///    is false or absent: the widening direction the narrowing rule
    ///    refuses, the write-side twin of FUARAN094's first shape;
    ///  - the grid is editable and a column is editable, but nothing names
    ///    where the edit goes: no `editStateKey`, and a `source` that is not a
    ///    direct `Binding.State` for the 663 write-back to land in.
    ///
    /// The second is deliberately NARROWER than FUARAN090, which warns about
    /// the same source shape at grid level. Where a column has explicitly
    /// declared itself editable the author has said something specific that
    /// cannot happen, so it is an Error rather than an advisory.
    | UneditableColumnDeclared of nodeId: string * columnLabel: string * reason: EditDefect
    /// **FUARAN097 (Error)**. A chart declares a TEMPORAL x-axis
    /// (`ChartSpec.XScale = Temporal`, Phase 882) over an x column whose
    /// statically-known type is not a date (`date` / `timestamp`) — the
    /// declaration cannot be honoured, so every row's x would read as the epoch
    /// and the chart would draw every point stacked on one date.
    ///
    /// The rule exists because the temporal axis is DECLARED rather than
    /// inferred, and a declaration the data contradicts is exactly what a
    /// grounded validator is for: the alternative postures were silent coercion
    /// (a flat wrong picture) and inference from the cell strings (a guess
    /// dressed as a rule). Fires only where the schema is statically known —
    /// FUARAN086's window, an `Embedded` table with an EMPTY pipeline — so an
    /// unknowable source passes ungrounded per the fuaran-core#90 rule: refuse
    /// only what is PROVABLY wrong. Carries the chart node's id, the x field,
    /// and the offending column-type tag.
    | ChartTemporalXNotDate of nodeId: string * field: string * columnType: string
    /// **FUARAN098 (Warning)**. An `Action.SetState` writes a state key that
    /// NOTHING in the tree reads (Phase 932) — the general form of the fake
    /// affordance [Phase 866](../../roadmap/phases/866-affordance-to-op-charter.md)
    /// chartered and [Phase 860](../../roadmap/phases/860-grid-behaviour-vocabulary-charter.md)
    /// deferred to it: a gesture the user can perform that changes nothing they
    /// can see. 866's property is that a declared affordance is real iff the node
    /// hosting the gesture also consumes its effect, or names in its own payload
    /// the slot that does; this is the residual authored shape, the tiers above
    /// and below it being structural and unenforceable respectively.
    ///
    /// **WARNING rather than Error, and that is load-bearing.** A key may
    /// legitimately be written for a HOST to read, and the validator cannot see
    /// the host. An Error would make a legal composition unshippable; a Warning
    /// that is occasionally wrong is useful, whereas an Error that is
    /// occasionally wrong gets suppressed, and a suppressed rule protects
    /// nothing. For the same reason the rule stands down entirely on a tree
    /// holding an OPAQUE reader (`Binding.Computed`, `NodeKind.Custom`), where
    /// the absence of a read proves nothing. Host-reserved keys are exempt via
    /// the Phase 782 prefix — such a write is REFUSED at dispatch, so its defect
    /// is that it is unaddressable, not that it is unread.
    ///
    /// Carries the writing node's id and the key. What counts as a READ is
    /// enumerated on `BindingWalk.StateKeyFacts`, not left to the reader.
    | SetStateNoReader of nodeId: string * key: string
    /// **FUARAN102 (Warning)**. A labelled datum names the CURRENT instant in
    /// its own words — "today", "last updated", "current date" — and states a
    /// hardcoded ISO-8601 calendar date beside it, where the host-furnished
    /// `Binding.Now` is what the author meant. A candidate finding: the tree
    /// renders a date that was true when it was written and is wrong from the
    /// next day onward, silently, with nothing to notice it.
    ///
    /// **The heuristic is deliberately narrow, and the narrowing is the
    /// design.** It requires BOTH halves on one node — a present-tense cue AND
    /// a bare date literal — because either alone is ordinary: a historical
    /// date carries no cue ("Founded 1994-03-02"), and a cue with no date is
    /// prose. That pairing is also what makes the "obviously-historical"
    /// distinction without consulting a clock, which is the point: the
    /// validator is pure and Fable-portable, and a rule whose verdict changed
    /// with the calendar would pass in CI today and fail in CI next quarter for
    /// no reason a reader could reconstruct. Few false positives beats
    /// coverage; a Warning that is occasionally wrong gets suppressed, and a
    /// suppressed rule protects nothing.
    ///
    /// Scoped to the "one labelled datum" kinds — `Fact`, `Metric`, `Badge`,
    /// `Callout`, `Heading`, `LabelValueRow`. `Markdown` and `List` are
    /// deliberately OUT: long-form prose is exactly where a legitimately
    /// historical date sits next to a present-tense sentence.
    ///
    /// Carries the node id and the literal that holds the date.
    | DateLiteralWhereNowPlausible of nodeId: string * literal: string
    /// **FUARAN103 (Warning)**. A `NodeKind.Switch` selects its branch on a
    /// state key that NOTHING in the tree can write (Phase 768) — the read-side
    /// twin of FUARAN098's fake affordance, and the shape every emission in
    /// this rule's source cluster had: a `Switch` correctly expressing "show a
    /// different branch when X changes", wired to an X no emittable surface
    /// could ever set, so one branch renders forever.
    ///
    /// Distinct from FUARAN083, which catches the EMPTY key — a malformed
    /// selector rather than an unreachable one.
    ///
    /// **WARNING, and it stands down under any opaque writer.** A host may
    /// write the key directly, and the validator cannot see the host; more
    /// sharply, any closure in the tree — a control's `onChange`, a grid's
    /// button cell, an `Action.Dispatch` — produces an arbitrary action at
    /// dispatch time and may write anything. `BindingWalk.StateKeyFacts`
    /// enumerates both the write surfaces counted and the opacity that silences
    /// the rule, so neither is left to the reader. Host-reserved keys (the
    /// Phase 782 prefix) are exempt: those are the host's to write by
    /// definition.
    ///
    /// Carries the switch node's id and the key.
    | SwitchKeyNoWriter of nodeId: string * key: string
    /// **FUARAN099 (Error)**. A `FieldRule.compare` reads a `State` key that no
    /// field in the enclosing form owns and nothing in the tree writes (Phase
    /// 864) — the predicate can never be satisfied or unsatisfied, only absent,
    /// so the field it guards is unconstrained while reading as constrained.
    ///
    /// **ERROR, where its two siblings are Warnings, and the asymmetry is the
    /// fuaran-core#90 rule rather than an inconsistency.** A dangling state key
    /// is decidable from the tree ALONE: the form's own fields are in hand, the
    /// tree's writers are in hand, and a cross-field predicate that names
    /// neither is wrong on the evidence the walk already holds. The other two
    /// rules turn on what a HOST might honour, which the walk cannot see.
    ///
    /// It still stands down under an opaque writer, for the same reason
    /// FUARAN103 does: any closure in the tree produces an arbitrary action at
    /// dispatch time, so "nothing writes this key" stops being provable.
    /// Host-reserved keys (the Phase 782 prefix) are exempt — the host's to
    /// write by definition.
    ///
    /// Carries the form node's id, the field declaring the rule, and the key.
    | CompareKeyUnreachable of nodeId: string * fieldId: string * key: string
    /// **FUARAN100 (Warning)**. A `FieldRule` slot the field's control cannot
    /// honour (Phase 864) — a `pattern` on a `Checkbox`, a `format` on a
    /// `TextArea`. Dead intent: the author declared a constraint, the tree
    /// carries it, and no renderer has anywhere to put it.
    ///
    /// **WARNING rather than Error**, because the projection is the host's:
    /// a native surface may honour a length bound on a control an HTML renderer
    /// cannot, and refusing the tree outright would decide that for every host.
    ///
    /// Carries the form node's id, the field id, the slot, and the control name.
    | RuleSlotUnhonourable of nodeId: string * fieldId: string * slot: RuleSlot * control: string
    /// **FUARAN101 (Warning)**. A `FieldRule.compare` against a LITERAL that
    /// duplicates a bound the control already carries (Phase 864) — a `gte`
    /// against a static number on a `RangedNumber` that already declares `min`.
    /// Two sources for one bound, free to disagree, and nothing decides which
    /// wins.
    ///
    /// This is the enforcement half of the charter's reuse rule — the rule slot
    /// never duplicates a bound the control already holds — and the reason
    /// `compare` is not itself a duplicate is that its operand is a `Binding`
    /// where the control's bound is a literal. A `Binding.Static` in the slot is
    /// still LEGAL; it is precisely the shape that collapses that distinction,
    /// which is why it warns rather than being refused at decode.
    ///
    /// Carries the form node's id, the field id, and the duplicated bound.
    | CompareDuplicatesBound of nodeId: string * fieldId: string * bound: string

/// Which `FieldRule` slot a control cannot honour (FUARAN100, Phase 864).
/// Typed rather than a string so the honourable set stays enumerable: a slot
/// added to `FieldRule` without a decision here will not compile.
and [<RequireQualifiedAccess>] RuleSlot =
    | Format
    | Pattern
    | MinLength
    | MaxLength

/// Why an editable column has nowhere to commit (FUARAN095, Phase 863).
and [<RequireQualifiedAccess>] EditDefect =
    /// The grid itself is not editable, so a column cannot turn editing on.
    | GridNotEditable
    /// No `editStateKey`, and the source is not a direct `Binding.State`.
    | NoReachableDestination

/// Why a sort declaration cannot be honoured (FUARAN094, Phase 861). Typed
/// rather than a string so the three shapes stay enumerable and a fourth
/// cannot be added by prose.
and [<RequireQualifiedAccess>] SortDefect =
    /// `sortable: true` on a column, but the grid names no `sortStateKey`.
    | NoSortStateKey of columnLabel: string
    /// `sortable: true` on a column with no `field` to order by.
    | ColumnHasNoField of columnLabel: string
    /// `defaultSort` names a column index outside the column set.
    | DefaultSortColumnOutOfRange of column: int * columnCount: int

/// Render a defect as its stable (code, severity, message) triple — the ONE
/// projection every consumer shares (the .NET validator oracle, certification
/// counterexamples, and Fable-side hosts surfacing advisories to a model).
/// Exhaustive by construction: a new defect case cannot ship without its code.
/// Severity of a described defect. Local to this module (not `Fuaran.Core.Severity`)
/// so `Fuaran.UI` keeps its lean dependency set; the .NET validator maps it to the
/// Core type at its own boundary.
[<RequireQualifiedAccess>]
type DefectSeverity =
    | Error
    | Warning

let describe (d: PreEmitDefect) : string * DefectSeverity * string =
    match d with
    | PreEmitDefect.DuplicateNodeId(id, count) ->
        "FUARAN-DUP-ID", DefectSeverity.Error, sprintf "node id '%s' appears %d times" id count
    | PreEmitDefect.EmptyNodeId -> "FUARAN-EMPTY-ID", DefectSeverity.Error, "a node carries an empty id"
    | PreEmitDefect.EmptyCustomKindIdentifier(m, c) ->
        "FUARAN-EMPTY-CUSTOM",
        DefectSeverity.Error,
        sprintf "Custom node has empty moduleId='%s' / componentId='%s'" m c
    | PreEmitDefect.KindNotAdmitted(nodeId, kind, policy) ->
        "FUARAN104",
        DefectSeverity.Warning,
        sprintf "node '%s' is a '%s', which decode policy '%s' does not admit" nodeId kind policy
    | PreEmitDefect.CustomPropSchemaViolation(nodeId, moduleId, componentId, propDefects) ->
        "FUARAN068",
        DefectSeverity.Error,
        sprintf
            "custom node '%s' (%s/%s) violates its declared prop schema — %d prop defect(s)"
            nodeId
            moduleId
            componentId
            (List.length propDefects)
    | PreEmitDefect.TabHeaderCountMismatch(nodeId, headerCount, childrenCount) ->
        "FUARAN047",
        DefectSeverity.Error,
        sprintf
            "tabs '%s' declares %d headers but %d children — the renderer aligns headers 1:1 with children by index"
            nodeId
            headerCount
            childrenCount
    | PreEmitDefect.TabTagCountMismatch(nodeId, tagCount, childrenCount) ->
        "FUARAN048",
        DefectSeverity.Error,
        sprintf
            "tabs '%s' declares %d tags but %d children — the tag → index round-trip needs parity"
            nodeId
            tagCount
            childrenCount
    | PreEmitDefect.TabActiveTagWithoutTags nodeId ->
        "FUARAN049",
        DefectSeverity.Warning,
        sprintf "tabs '%s' sets ActiveTag but TabTags = None — the tag binding has nothing to resolve against" nodeId
    | PreEmitDefect.DecorativeFilter(declaringNodeId, name) ->
        "FUARAN074",
        DefectSeverity.Warning,
        sprintf
            "filter '%s' (declared on '%s') is consumed by nothing — no Binding.Filter read, Query.dependsOn, or Transform param references it"
            name
            declaringNodeId
    | PreEmitDefect.DanglingFilterReference(readerNodeId, name) ->
        "FUARAN075",
        DefectSeverity.Error,
        sprintf
            "'%s' declares a filter edge on '%s' (dependsOn / Transform param source) but no Filters chip declares that name"
            readerNodeId
            name
    | PreEmitDefect.UnreferencedTransformParam(readerNodeId, name) ->
        "FUARAN076",
        DefectSeverity.Warning,
        sprintf "'%s' declares Transform param '%s' but the pipeline never references it (paramsOf)" readerNodeId name
    | PreEmitDefect.BlankGridColumn(nodeId, columnLabel) ->
        "FUARAN077",
        DefectSeverity.Warning,
        sprintf "grid '%s' column '%s' has neither a value closure nor a field — it renders blank" nodeId columnLabel
    | PreEmitDefect.UnstableRowIdentity nodeId ->
        "FUARAN078",
        DefectSeverity.Warning,
        sprintf "grid '%s' has neither rowKey nor rowKeyField — no stable row identity" nodeId
    | PreEmitDefect.OrphanQueryFetch(readerNodeId, queryName) ->
        "FUARAN072",
        DefectSeverity.Warning,
        sprintf
            "'%s' calls into Query '%s' but no Binding.Query in the tree reads that slot — an orphan fetch (name typo?)"
            readerNodeId
            queryName
    | PreEmitDefect.CallResultDropped(readerNodeId, endpoint) ->
        "FUARAN073",
        DefectSeverity.Warning,
        sprintf
            "'%s' calls '%s' with neither an onResult closure nor an into target — the response is dropped (fine for a command endpoint; add into for data)"
            readerNodeId
            endpoint
    | PreEmitDefect.DanglingSelection(readerNodeId, target) ->
        "FUARAN070",
        DefectSeverity.Error,
        sprintf
            "'%s' reads Binding.Selection on '%s' but no node with that id exists — point the binding at the selection-producing node's id"
            readerNodeId
            target
    | PreEmitDefect.SelectionOverNonProducer(readerNodeId, target) ->
        "FUARAN071",
        DefectSeverity.Warning,
        sprintf
            "'%s' reads Binding.Selection on '%s', which is not a selection-producing (Visualisation) node — nothing in the tree will write that selection"
            readerNodeId
            target
    | PreEmitDefect.DuplicateWriteBackKey(stateKey, writers) ->
        "FUARAN085",
        DefectSeverity.Warning,
        sprintf
            "state key '%s' has %d handler-free write-back writers (%s) — typing in one silently overwrites the other's captured value; give each field its own key"
            stateKey
            (List.length writers)
            (writers
             |> List.map (fun (nid, fid) -> sprintf "%s/%s" nid fid)
             |> String.concat ", ")
    | PreEmitDefect.InertControl(nodeId, control) ->
        "FUARAN069",
        DefectSeverity.Warning,
        sprintf
            "%s on '%s' has no event handler and no writable value binding — bind its value to $state.<key> / $filters.<name>, or supply the handler (Phase 426 write-back default)"
            control
            nodeId
    | PreEmitDefect.DuplicateSwitchMatch(nodeId, matchValue) ->
        "FUARAN082",
        DefectSeverity.Error,
        sprintf
            "Switch '%s' has two or more cases matching '%s' — first-match-wins makes the later case dead; give each case a distinct match value (Phase 392)"
            nodeId
            matchValue
    | PreEmitDefect.UngroundedSwitchStateKey nodeId ->
        "FUARAN083",
        DefectSeverity.Warning,
        sprintf
            "Switch '%s' has an empty stateKey — it can never resolve a case and is stuck on its default; name the state key the switch selects on (Phase 392)"
            nodeId
    | PreEmitDefect.ChartFieldUngrounded(nodeId, field) ->
        "FUARAN086",
        DefectSeverity.Error,
        sprintf
            "chart '%s' references field '%s' absent from its statically-known data schema — it would lower silently flat/empty (Phase 640)"
            nodeId
            field
    | PreEmitDefect.ChartFieldTypeMismatch(nodeId, field, columnType) ->
        "FUARAN087",
        DefectSeverity.Error,
        sprintf
            "chart '%s' plots field '%s' of type '%s' — the lowering reads non-numeric cells as 0.0, a silently flat series (Phase 640)"
            nodeId
            field
            columnType
    | PreEmitDefect.ChartPieSeriesShape(nodeId, seriesCount) ->
        "FUARAN088",
        DefectSeverity.Error,
        sprintf
            "pie chart '%s' declares %d series — the pie lowering refuses anything but exactly one (no silent truncation; Phase 638/640)"
            nodeId
            seriesCount
    | PreEmitDefect.ChartStackedMeaningless(nodeId, kind) ->
        "FUARAN089",
        DefectSeverity.Warning,
        sprintf
            "chart '%s' sets Stacked=true on kind %s — the lowering ignores it (dead intent; Phase 637/640)"
            nodeId
            kind
    | PreEmitDefect.InertEditableGrid nodeId ->
        "FUARAN090",
        DefectSeverity.Warning,
        sprintf
            "grid '%s' sets editable=true but its source is not a direct $state binding — edits have nowhere to go, every cell renders read-only; source the grid (and any chart that should track edits) from a shared {\"$type\":\"State\",\"key\":…,\"default\":[rows]} binding"
            nodeId
    | PreEmitDefect.MaxDepthExceeded(nodeId, limit) ->
        "FUARAN091",
        DefectSeverity.Error,
        sprintf
            "node '%s' nests deeper than the wire limit MaxDepth = %d (WIRE_FORMAT §21) — the tree was not walked past this point; flatten the nesting"
            nodeId
            limit
    | PreEmitDefect.ProtectedNonMailtoLink nodeId ->
        "FUARAN092",
        DefectSeverity.Warning,
        sprintf
            "link '%s' sets protection=\"email\" on a non-mailto href — the Email strategy only protects a mailto: address, so the renderers ignore the flag (dead intent); drop the protection or point the href at mailto:<address>"
            nodeId
    | PreEmitDefect.PageSizeWithoutPageKey nodeId ->
        "FUARAN093",
        DefectSeverity.Error,
        sprintf
            "grid '%s' declares pageSize but no pageStateKey — nothing carries the page position, so the grid renders every row and the page size is dead intent; add pageStateKey naming the State key the pager writes {\"page\":N} to"
            nodeId
    | PreEmitDefect.DoublePagedGrid(nodeId, pageStateKey) ->
        "FUARAN096",
        DefectSeverity.Warning,
        sprintf
            "grid '%s' pages client-side on pageStateKey '%s' while its source is a query depending on that same key — the host already returns the page, so slicing it again would page the page; drop pageSize to let the host page, or drop the dependsOn to page client-side"
            nodeId
            pageStateKey
    | PreEmitDefect.UnhonourableSort(nodeId, reason) ->
        "FUARAN094",
        DefectSeverity.Error,
        (match reason with
         | SortDefect.NoSortStateKey label ->
             sprintf
                 "grid '%s' column '%s' declares sortable=true but the grid names no sortStateKey — a column narrows a behaviour, it cannot turn one on; add sortStateKey to the grid or drop the column flag"
                 nodeId
                 label
         | SortDefect.ColumnHasNoField label ->
             sprintf
                 "grid '%s' column '%s' declares sortable=true but has no field — nothing names the row property to order by; add field, or drop the flag and let the column render unsorted"
                 nodeId
                 label
         | SortDefect.DefaultSortColumnOutOfRange(column, count) ->
             sprintf
                 "grid '%s' declares defaultSort on column %d but the grid has %d column(s) — the declared order can never be applied; point it at an existing column index"
                 nodeId
                 column
                 count)
    | PreEmitDefect.UneditableColumnDeclared(nodeId, columnLabel, reason) ->
        "FUARAN095",
        DefectSeverity.Error,
        (match reason with
         | EditDefect.GridNotEditable ->
             sprintf
                 "grid '%s' column '%s' declares editable=true but the grid is not editable — a column narrows a behaviour, it cannot turn one on; set editable on the grid, or drop the column flag"
                 nodeId
                 columnLabel
         | EditDefect.NoReachableDestination ->
             sprintf
                 "grid '%s' column '%s' is editable but no destination is reachable — declare editStateKey, or source the grid from a direct {\"$type\":\"State\",\"key\":…} binding so the edit has somewhere to commit"
                 nodeId
                 columnLabel)
    | PreEmitDefect.ChartTemporalXNotDate(nodeId, field, columnType) ->
        "FUARAN097",
        DefectSeverity.Error,
        sprintf
            "chart '%s' declares a temporal x-axis over field '%s' of type '%s' — a date axis needs a date column, and every row's x would read as 1970-01-01; give the column type 'date' (canonical ISO-8601 YYYY-MM-DD cells), or drop xScale to plot the values as categories (Phase 882)"
            nodeId
            field
            columnType
    | PreEmitDefect.SetStateNoReader(nodeId, key) ->
        "FUARAN098",
        DefectSeverity.Warning,
        sprintf
            "'%s' writes state key '%s' but nothing in the tree reads it — the gesture runs and the user sees no change (a fake affordance); bind a reader to {\"$type\":\"State\",\"key\":\"%s\"}, select a Switch on it, or name it as a grid's sortStateKey/pageStateKey. If the key is written for the HOST to read, this warning is expected and can be ignored (Phase 932)"
            nodeId
            key
            key
    | PreEmitDefect.DateLiteralWhereNowPlausible(nodeId, literal) ->
        "FUARAN102",
        DefectSeverity.Warning,
        sprintf
            "'%s' names the current instant and states a hardcoded date: \"%s\" — the value was true when it was written and is wrong from the next day onward; bind the slot to {\"$type\":\"Now\"} (with a Format binding for the display shape) so the host furnishes the instant. If the date is genuinely historical, reword the label so it does not read as the present"
            nodeId
            literal
    | PreEmitDefect.SwitchKeyNoWriter(nodeId, key) ->
        "FUARAN103",
        DefectSeverity.Warning,
        sprintf
            "switch '%s' selects on state key '%s' but nothing in the tree can write it — one branch renders forever; give the key a writer (an Action.SetState on a button, a Call with into: {\"$type\":\"State\",\"key\":\"%s\"}, or a control write-back slot bound to it), or select on the binding that already changes (a Selection, a Filter, a Query). If the key is written by the HOST, this warning is expected and can be ignored"
            nodeId
            key
            key
    | PreEmitDefect.CompareKeyUnreachable(nodeId, fieldId, key) ->
        "FUARAN099",
        DefectSeverity.Error,
        sprintf
            "form '%s' field '%s' compares against state key '%s', but no field in the form owns that key and nothing in the tree writes it — the predicate can never be met or unmet, only absent, so the field reads as constrained and is not; point 'against' at a sibling field's id (a form field's value lives in State under its own id), or give the key a writer"
            nodeId
            fieldId
            key
    | PreEmitDefect.RuleSlotUnhonourable(nodeId, fieldId, slot, control) ->
        "FUARAN100",
        DefectSeverity.Warning,
        sprintf
            "form '%s' field '%s' declares %s on a %s control, which cannot honour it — the constraint is carried and never applied (dead intent); move the rule to a text control, or drop the slot. If a host you target DOES honour it, this warning is expected and can be ignored"
            nodeId
            fieldId
            (match slot with
             | RuleSlot.Format -> "rule.format"
             | RuleSlot.Pattern -> "rule.pattern"
             | RuleSlot.MinLength -> "rule.minLength"
             | RuleSlot.MaxLength -> "rule.maxLength")
            control
    | PreEmitDefect.CompareDuplicatesBound(nodeId, fieldId, bound) ->
        "FUARAN101",
        DefectSeverity.Warning,
        sprintf
            "form '%s' field '%s' compares against a LITERAL while its control already declares %s — two sources for one bound, free to disagree, and nothing decides which wins; drop the compare and keep the control's bound, or make the operand read something that changes ({\"$type\":\"State\",\"key\":\"<sibling field id>\"}), which is what the rule slot is for"
            nodeId
            fieldId
            bound

/// The shared walk behind `validate` / `validateWithRegistry`. `customCheck`
/// runs at every `NodeKind.Custom` (node id, moduleId, componentId, props) —
/// `validate` passes a no-op; `validateWithRegistry` passes the registry's
/// schema check. One walk, so the two entry points can never drift.

// ── FUARAN102 — a hardcoded date where the host's instant was meant ──
//
// Everything below is ASCII-only character scanning on purpose: `Fuaran.UI` is
// Fable-compiled and FSharp.Core-only, and the cues this rule matches are ASCII
// phrases. Hand-rolling the scan keeps one behaviour across every host tier
// rather than inheriting a runtime's culture rules, which is the same reasoning
// the canonical encoder is hand-written.

let private isAsciiDigit (c: char) = c >= '0' && c <= '9'

let private isWordChar (c: char) =
    isAsciiDigit c || (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z')

let private lowerAscii (c: char) =
    if c >= 'A' && c <= 'Z' then char (int c + 32) else c

/// True when an ISO-8601 calendar date (`YYYY-MM-DD`) starts at `i` — four
/// digits, a hyphen, a month in 01–12, a hyphen, a day in 01–31 — with no digit
/// immediately either side, so a part number like `12345-67-890` is not a date.
/// The day is range-checked but not calendar-checked: a validator that refused
/// `2026-02-30` here would be answering a different question.
let private isoDateAt (s: string) (i: int) : bool =
    let digitsAt start count =
        start + count <= s.Length
        && (let mutable ok = true

            for k in start .. start + count - 1 do
                if not (isAsciiDigit s[k]) then
                    ok <- false

            ok)

    let twoDigitValue start =
        (int s[start] - int '0') * 10 + (int s[start + 1] - int '0')

    i + 10 <= s.Length
    && digitsAt i 4
    && s[i + 4] = '-'
    && digitsAt (i + 5) 2
    && s[i + 7] = '-'
    && digitsAt (i + 8) 2
    && (let month = twoDigitValue (i + 5)
        let day = twoDigitValue (i + 8)
        month >= 1 && month <= 12 && day >= 1 && day <= 31)
    && (i = 0 || not (isAsciiDigit s[i - 1]))
    && (i + 10 = s.Length || not (isAsciiDigit s[i + 10]))

/// The first ISO-8601 date `s` holds, if any.
let private isoDateIn (s: string) : string option =
    // `IsNullOrEmpty` rather than `isNull`: a hand-built or wire-decoded record
    // can carry a null the type says cannot exist, and this spelling states that
    // without a file-scoped nullness suppression.
    if System.String.IsNullOrEmpty s then
        None
    else
        let mutable found = None
        let mutable i = 0

        while found.IsNone && i + 10 <= s.Length do
            if isoDateAt s i then
                found <- Some(s.Substring(i, 10))

            i <- i + 1

        found

/// Phrases that name the CURRENT instant rather than a stated one. Deliberately
/// small: a bare "now" is excluded because "Now available — 2026-09-01" is a
/// future event, not a stale clock, and one such false positive costs more than
/// several missed findings. "as of" alone is excluded for the same reason — it
/// prefaces a historical cut-off at least as often as a live one.
let private nowCuePhrases =
    [| "current date"
       "date generated"
       "generated on"
       "last updated"
       "right now"
       "as of now" |]

/// Single-word cues, matched on word boundaries so "today" catches "today's"
/// and does not catch a longer word that happens to contain it.
let private nowCueWords = [| "today" |]

let private containsAt (haystack: string) (needle: string) (i: int) =
    i + needle.Length <= haystack.Length
    && (let mutable ok = true

        for k in 0 .. needle.Length - 1 do
            if lowerAscii haystack[i + k] <> needle[k] then
                ok <- false

        ok)

/// True when `s` names the current instant in its own words.
let private carriesNowCue (s: string) : bool =
    if System.String.IsNullOrEmpty s then
        false
    else
        let mutable hit = false
        let mutable i = 0

        while not hit && i < s.Length do
            for phrase in nowCuePhrases do
                if containsAt s phrase i then
                    hit <- true

            for word in nowCueWords do
                if
                    containsAt s word i
                    && (i = 0 || not (isWordChar s[i - 1]))
                    && (i + word.Length = s.Length || not (isWordChar s[i + word.Length]))
                then
                    hit <- true

            i <- i + 1

        hit

/// The LITERAL text of a "one labelled datum" node — the kinds whose whole
/// purpose is to state a value beside its name, which is where a stale hardcoded
/// date does its damage. `Markdown` and `List` are deliberately absent: long-form
/// prose is exactly where a legitimately historical date sits beside a
/// present-tense sentence, and the rule would stop being trustworthy there.
let private datumLiterals (kind: NodeKind<'Msg>) : string list =
    let lit (t: TextSource) =
        match t with
        | TextSource.Literal s -> [ s ]
        | TextSource.Bound _
        | TextSource.I18n _ -> []

    let litOpt (t: TextSource option) =
        match t with
        | Some t -> lit t
        | None -> []

    match kind with
    | NodeKind.Heading h -> lit h.Text
    | NodeKind.Badge b -> lit b.Label
    | NodeKind.Callout c -> litOpt c.Heading @ lit c.Body
    | NodeKind.Fact f -> lit f.Label @ lit f.Value @ litOpt f.Help
    | NodeKind.Metric m -> lit m.Label @ litOpt m.Subtext
    | NodeKind.LabelValueRow r -> lit r.Label @ litOpt r.Help
    | _ -> []

/// FUARAN102's verdict for one node: the offending literal when the node both
/// names the current instant and states a hardcoded date. A `Now`-bound slot
/// carries no literal at all, so it cannot reach this.
let private staleDateLiteral (kind: NodeKind<'Msg>) : string option =
    let literals = datumLiterals kind

    if literals |> List.exists carriesNowCue then
        literals |> List.tryFind (fun s -> (isoDateIn s).IsSome)
    else
        None

/// A binding the Phase 426 control write-back default can write to: directly
/// `Binding.State` (the renderer writes the StateStore slot) or
/// `Binding.Filter` (the FilterStore slot). Any other shape gives an omitted
/// handler nothing to write — the FUARAN069 inert-control condition. A
/// `Binding.Local` also counts as live: its Phase 62 commit pipeline carries
/// the change independently of the handler.
let private isWriteBackTarget (binding: Binding<'T>) : bool =
    match binding with
    | Binding.State _
    | Binding.Filter(_, None)
    | Binding.Local _ -> true
    | _ -> false

let private validateCore
    (policy: DecodePolicy)
    (customCheck: string -> string -> string -> Map<string, JVal> -> PreEmitDefect option)
    (node: Node<'Msg>)
    : Result<unit, PreEmitDefect list> =
    let defects = ResizeArray<PreEmitDefect>()
    // Phase 596 (FUARAN085) — (stateKey, nodeId, fieldId) per handler-free
    // form-field write-back; duplicates surface post-walk.
    let writeBackKeys = ResizeArray<string * string * string>()
    // Phase 864 (FUARAN099) — (nodeId, fieldId, key) per `FieldRule.compare`
    // reading a `Binding.State`, paired with the set of keys the ENCLOSING form
    // itself owns. Both are collected here and judged post-walk, because the
    // second half of the question ("nothing in the tree writes it") is only
    // answerable once the whole tree has been seen.
    let compareStateReads = ResizeArray<string * string * string>()
    let formOwnedStateKeys = System.Collections.Generic.HashSet<string>()
    let nodeIdCounts = System.Collections.Generic.Dictionary<string, int>()

    let recordNodeId (raw: string) =
        if raw = "" then
            defects.Add PreEmitDefect.EmptyNodeId
        else
            match nodeIdCounts.TryGetValue raw with
            | true, n -> nodeIdCounts[raw] <- n + 1
            | false, _ -> nodeIdCounts[raw] <- 1

    // Phase 781 — the depth bound on this walk. `walkBody` below is the
    // pre-existing recursion; `walk` is the counter around it, so the guard sits
    // on every recursion site at once rather than on the forty-odd `List.iter
    // walk` calls individually. Measured, this walk overflows the .NET default
    // 1 MB stack at 294 levels in Release and 151 in Debug, so an unbounded tree
    // took the process down with a `StackOverflowException` — uncatchable, hence
    // no defect list, hence no "structured error, never an exception".
    //
    // A local mutable is the right shape here rather than a threaded parameter:
    // `walk` is a closure created fresh per `validateCore` call, so the counter
    // is per-invocation and cannot be shared across threads.
    let mutable depth = 0
    // One defect, not one per over-deep node — an over-deep subtree would
    // otherwise emit thousands of identical entries and bury the real report.
    let mutable depthReported = false

    let rec walk (n: Node<'Msg>) =
        depth <- depth + 1

        if depth > WireLimits.MaxDepth then
            if not depthReported then
                depthReported <- true
                defects.Add(PreEmitDefect.MaxDepthExceeded(n.Id, WireLimits.MaxDepth))
        else
            walkBody n

        depth <- depth - 1

    and walkBody (n: Node<'Msg>) =
        recordNodeId n.Id

        // FUARAN104 (Phase 1020) — the decode-time admission policy, mirrored
        // at the authoring end. Guarded on `narrows` so the shipped default
        // costs one branch per tree rather than a set lookup per node, and so
        // the defect is unreachable for every caller that declares nothing.
        if DecodePolicy.narrows policy then
            let wireKind = wireKindName n.Kind

            if not (DecodePolicy.admits policy wireKind) then
                defects.Add(PreEmitDefect.KindNotAdmitted(n.Id, wireKind, policy.Identity))

        // FUARAN102 (Phase 765) — a labelled datum that names the current
        // instant and states a hardcoded date. Per-node and purely lexical; the
        // scope and the deliberate narrowing are on the defect case.
        match staleDateLiteral n.Kind with
        | Some literal -> defects.Add(PreEmitDefect.DateLiteralWhereNowPlausible(n.Id, literal))
        | None -> ()

        // Per-kind: check kind-specific invariants + enumerate children.
        match n.Kind with
        // -- Layout --
        | NodeKind.Box spec -> spec.Children |> List.iter walk
        | NodeKind.SplitPanel spec -> spec.Children |> List.iter walk
        | NodeKind.Tabs spec ->
            // FUARAN047 / FUARAN048 / FUARAN049
            // tabs-shape invariants. Length mismatches are construction
            // defects (the renderer would silently drop headers or tags
            // past the children boundary); ActiveTag-without-TabTags
            // is a semantic mistake (the tag binding has nowhere to
            // resolve to). The `NodeId raw` extraction matches the
            // existing `recordNodeId` pattern.
            let nodeIdStr = n.Id
            let childrenCount = spec.Children.Length

            match spec.TabHeaders with
            | Some hs when hs.Length <> childrenCount ->
                defects.Add(PreEmitDefect.TabHeaderCountMismatch(nodeIdStr, hs.Length, childrenCount))
            | _ -> ()

            match spec.TabTags with
            | Some ts when ts.Length <> childrenCount ->
                defects.Add(PreEmitDefect.TabTagCountMismatch(nodeIdStr, ts.Length, childrenCount))
            | _ -> ()

            match spec.ActiveTag, spec.TabTags with
            | Some _, None -> defects.Add(PreEmitDefect.TabActiveTagWithoutTags nodeIdStr)
            | _ -> ()

            // FUARAN069 (Phase 426): tabs are live when either channel can
            // carry a click — a handler, or a writable slot the write-back
            // default targets (integer: ActiveIndex; tag overlay:
            // ActiveTag when TabTags is populated).
            let indexLive = spec.OnSelect.IsSome || isWriteBackTarget spec.ActiveIndex

            let tagLive =
                match spec.OnSelectTag, spec.TabTags, spec.ActiveTag with
                | Some _, Some _, _ -> true
                | None, Some _, Some tagBinding -> isWriteBackTarget tagBinding
                | _ -> false

            if not (indexLive || tagLive) then
                defects.Add(PreEmitDefect.InertControl(nodeIdStr, "Tabs"))

            spec.Children |> List.iter walk
        | NodeKind.Stepper spec -> spec.Children |> List.iter walk
        | NodeKind.SummaryList spec -> spec.Children |> List.iter walk
        | NodeKind.Disclosure spec ->
            // FUARAN069 (Phase 426): no toggle handler and no writable
            // `Open` slot — the model never hears the native toggle.
            let nodeIdStr = n.Id

            if spec.OnToggle.IsNone && not (isWriteBackTarget spec.Open) then
                defects.Add(PreEmitDefect.InertControl(nodeIdStr, "Disclosure"))

            spec.Children |> List.iter walk
        | NodeKind.Modal spec ->
            // FUARAN069 (Phase 426): a dismissable modal with no dismiss
            // action and no writable `Open` slot can never close.
            let nodeIdStr = n.Id

            if spec.Dismissable && spec.OnDismiss.IsNone && not (isWriteBackTarget spec.Open) then
                defects.Add(PreEmitDefect.InertControl(nodeIdStr, "Modal"))

            spec.Children |> List.iter walk
        | NodeKind.ScrollArea spec -> spec.Children |> List.iter walk
        | NodeKind.DataGrid(spec) ->
            // FUARAN077 / FUARAN078 (Phase 425 follow-up): the declarative grid
            // display floor — every column needs a Value closure or a Field,
            // and the grid needs a RowKey closure or a RowKeyField for stable
            // row identity (the 427 selected-row state keys off it).
            let nodeIdStr = n.Id

            for col in spec.Columns do
                if col.Value.IsNone && col.Field.IsNone then
                    defects.Add(PreEmitDefect.BlankGridColumn(nodeIdStr, col.Label))

            if spec.RowKey.IsNone && spec.RowKeyField.IsNone then
                defects.Add(PreEmitDefect.UnstableRowIdentity nodeIdStr)

            // FUARAN090 (Phase 663): `editable: true` only means anything when the
            // grid's source is a direct `Binding.State` — the renderer's grid
            // write-back slot. Any other source (Transform / Static / Query /
            // staticRows mode) renders read-only, so the flag is dead intent.
            let editableWritable =
                match spec.Source with
                | Binding.State _ -> true
                | _ -> false

            // Phase 863 widened what "writable" means: a declared
            // `editStateKey` IS a destination, so a grid carrying one is no
            // longer inert and FUARAN090 must not fire. Leaving it would report
            // the very shape 863 added as dead intent.
            if spec.Editable && not editableWritable && spec.EditStateKey.IsNone then
                defects.Add(PreEmitDefect.InertEditableGrid nodeIdStr)

            // FUARAN093 / FUARAN096 (Phase 862): the two authored shapes that
            // can still declare paging that does not page. The decorative-pager
            // shape itself needs no rule — the pager is renderer-owned, so a
            // control writing state nothing reads is not authorable.
            // FUARAN094 (Phase 861): a sort declaration that cannot be
            // honoured. The narrowing rule is directional — a column may turn
            // a behaviour OFF, never on — so `sortable: true` under a grid
            // with no sort state key is refused rather than silently ignored.
            for col in spec.Columns do
                match col.Sortable with
                | Some true when spec.SortStateKey.IsNone ->
                    defects.Add(PreEmitDefect.UnhonourableSort(nodeIdStr, SortDefect.NoSortStateKey col.Label))
                | Some true when col.Field.IsNone ->
                    defects.Add(PreEmitDefect.UnhonourableSort(nodeIdStr, SortDefect.ColumnHasNoField col.Label))
                | _ -> ()

            match spec.DefaultSort with
            | Some ds when ds.Column >= List.length spec.Columns ->
                defects.Add(
                    PreEmitDefect.UnhonourableSort(
                        nodeIdStr,
                        SortDefect.DefaultSortColumnOutOfRange(ds.Column, List.length spec.Columns)
                    )
                )
            | _ -> ()

            // FUARAN095 (Phase 863): the write side's twin of FUARAN094. The
            // destination is reachable when `editStateKey` names one, or when
            // the 663 write-back can land in the grid's own State source.
            let destinationReachable =
                spec.EditStateKey.IsSome
                || (match spec.Source with
                    | Binding.State _ -> true
                    | _ -> false)

            for col in spec.Columns do
                let columnEditable =
                    match col.Editable with
                    | Some v -> v
                    | None -> spec.Editable

                if col.Editable = Some true && not spec.Editable then
                    defects.Add(
                        PreEmitDefect.UneditableColumnDeclared(nodeIdStr, col.Label, EditDefect.GridNotEditable)
                    )
                elif col.Editable = Some true && columnEditable && not destinationReachable then
                    defects.Add(
                        PreEmitDefect.UneditableColumnDeclared(nodeIdStr, col.Label, EditDefect.NoReachableDestination)
                    )

            match spec.PageSize, spec.PageStateKey with
            | Some _, None -> defects.Add(PreEmitDefect.PageSizeWithoutPageKey nodeIdStr)
            | Some _, Some key ->
                let hostPages =
                    match spec.Source with
                    | Binding.Query(_, _, Some deps) -> deps |> List.contains key
                    | _ -> false

                if hostPages then
                    defects.Add(PreEmitDefect.DoublePagedGrid(nodeIdStr, key))
            | None, _ -> ()
        // Display kinds are leaves; future kind-specific invariants (e.g.
        // HeadingLevel ∈ [1..6]) land here.
        | NodeKind.Heading _
        | NodeKind.Markdown _
        | NodeKind.Metric _
        | NodeKind.Badge _
        | NodeKind.Sparkline _
        | NodeKind.Callout _
        | NodeKind.Progress _
        | NodeKind.Skeleton _
        | NodeKind.Icon _
        | NodeKind.LabelValueRow _
        | NodeKind.Fact _
        | NodeKind.Image _
        | NodeKind.List _
        | NodeKind.Toast _
        | NodeKind.CodeBlock _
        | NodeKind.Math _
        | NodeKind.Drawing _ -> ()
        // FUARAN092 (Phase 812): email protection declared over an href that
        // is statically known not to be a mailto:. Bound hrefs (Query / State
        // / …) resolve at runtime and are not judged here.
        | NodeKind.Link spec ->
            match spec.Protection, spec.Href with
            | Some LinkProtection.Email, Binding.Static(Some href) when
                not (href.StartsWith("mailto:", System.StringComparison.Ordinal))
                ->
                defects.Add(PreEmitDefect.ProtectedNonMailtoLink n.Id)
            | Some LinkProtection.Email, Binding.Static None -> defects.Add(PreEmitDefect.ProtectedNonMailtoLink n.Id)
            | _ -> ()
        // FUARAN069 (Phase 426): an interactive input whose handler is
        // omitted needs a writable value binding for the write-back
        // default to target; anything else is an inert control. Filter
        // chips are exempt — a handler-free chip always writes its own
        // `$filters.<name>` (Phase 423), so it can never be inert.
        | NodeKind.Form spec ->
            let nodeIdStr = n.Id

            let checkField (field: FormField<'Msg>) =
                // Phase 596 (FUARAN085): a handler-free field whose value is
                // directly `State(key, _)` WRITES that key — record it so
                // post-walk we can flag two writers on one key. An OMITTED
                // value slot (`None`) is the Phase 596 symmetric auto-bind and
                // writes `$state.<field id>` — record the field id as the key
                // (this is the shape a decoded / AI-authored field takes).
                let recordWriteBack (value: Binding<'v> option) (handlerAbsent: bool) =
                    if handlerAbsent then
                        match value with
                        | Some(Binding.State(key, _)) -> writeBackKeys.Add(key, nodeIdStr, field.Id)
                        | None -> writeBackKeys.Add(field.Id, nodeIdStr, field.Id)
                        | Some _ -> ()

                (match field.Kind with
                 | FormFieldKind.Text(value, oc) -> recordWriteBack value oc.IsNone
                 | FormFieldKind.Number(value, oc) -> recordWriteBack value oc.IsNone
                 | FormFieldKind.Checkbox(value, ot) -> recordWriteBack value ot.IsNone
                 | FormFieldKind.Toggle(value, ot) -> recordWriteBack value ot.IsNone
                 | FormFieldKind.Choice(_, value, oc) -> recordWriteBack value oc.IsNone
                 | FormFieldKind.TextArea(value, oc, _) -> recordWriteBack value oc.IsNone
                 | FormFieldKind.RangedNumber(value, oc, _, _, _) -> recordWriteBack value oc.IsNone
                 | FormFieldKind.Range(value, oc, _, _, _) -> recordWriteBack value oc.IsNone
                 | FormFieldKind.SegmentedChoice(_, value, oc, _) -> recordWriteBack value oc.IsNone
                 | FormFieldKind.Date(value, oc, _, _, _, _) -> recordWriteBack value oc.IsNone
                 | FormFieldKind.DateRange(value, oc, _, _, _, _) -> recordWriteBack value oc.IsNone)

                // ── Phase 864 — the declared-rule family (FUARAN099/100/101) ──
                //
                // A field OWNS a state key when the key is its own id (the
                // auto-bind puts its value there) or its value binding names
                // one directly. Recorded for every field, rule or no rule,
                // because a compare on field A is satisfied by field B's
                // ownership and the two are seen in either order.
                formOwnedStateKeys.Add field.Id |> ignore

                let recordOwnedKey (value: Binding<'v> option) =
                    match value with
                    | Some(Binding.State(key, _)) -> formOwnedStateKeys.Add key |> ignore
                    | _ -> ()

                (match field.Kind with
                 | FormFieldKind.Text(value, _) -> recordOwnedKey value
                 | FormFieldKind.Number(value, _) -> recordOwnedKey value
                 | FormFieldKind.Checkbox(value, _) -> recordOwnedKey value
                 | FormFieldKind.Toggle(value, _) -> recordOwnedKey value
                 | FormFieldKind.Choice(_, value, _) -> recordOwnedKey value
                 | FormFieldKind.TextArea(value, _, _) -> recordOwnedKey value
                 | FormFieldKind.RangedNumber(value, _, _, _, _) -> recordOwnedKey value
                 | FormFieldKind.Range(value, _, _, _, _) -> recordOwnedKey value
                 | FormFieldKind.SegmentedChoice(_, value, _, _) -> recordOwnedKey value
                 | FormFieldKind.Date(value, _, _, _, _, _) -> recordOwnedKey value
                 | FormFieldKind.DateRange(value, _, _, _, _, _) -> recordOwnedKey value)

                match field.Rule with
                | None -> ()
                | Some rule ->
                    // What this control can honour. `compare` is absent from the
                    // table on purpose: it is a comparison of the field's VALUE,
                    // which every control has. The name is the one a message
                    // shows the author, so it is the wire discriminator.
                    let control, honoursFormat, honoursTextBounds =
                        match field.Kind with
                        | FormFieldKind.Text _ -> "Text", true, true
                        | FormFieldKind.TextArea _ -> "TextArea", false, true
                        | FormFieldKind.Number _ -> "Number", false, false
                        | FormFieldKind.Checkbox _ -> "Checkbox", false, false
                        | FormFieldKind.Toggle _ -> "Toggle", false, false
                        | FormFieldKind.Choice _ -> "Choice", false, false
                        | FormFieldKind.RangedNumber _ -> "RangedNumber", false, false
                        | FormFieldKind.Range _ -> "Range", false, false
                        | FormFieldKind.SegmentedChoice _ -> "SegmentedChoice", false, false
                        | FormFieldKind.Date _ -> "Date", false, false
                        | FormFieldKind.DateRange _ -> "DateRange", false, false

                    let unhonourable (slot: RuleSlot) =
                        defects.Add(PreEmitDefect.RuleSlotUnhonourable(nodeIdStr, field.Id, slot, control))

                    if rule.Format.IsSome && not honoursFormat then
                        unhonourable RuleSlot.Format

                    if rule.Pattern.IsSome && not honoursTextBounds then
                        unhonourable RuleSlot.Pattern

                    if rule.MinLength.IsSome && not honoursTextBounds then
                        unhonourable RuleSlot.MinLength

                    if rule.MaxLength.IsSome && not honoursTextBounds then
                        unhonourable RuleSlot.MaxLength

                    match rule.Compare with
                    | None -> ()
                    | Some cmp ->
                        match cmp.Against with
                        | Binding.State(key, _) when key <> "" -> compareStateReads.Add(nodeIdStr, field.Id, key)
                        | Binding.Static _ ->
                            // FUARAN101 — the operand is a literal, so the only
                            // question is whether the control already declares
                            // the equivalent bound. `gte`/`gt` duplicate a
                            // lower bound, `lte`/`lt` an upper one; `eq`/`neq`
                            // duplicate neither and are silent.
                            let lower, upper =
                                match field.Kind with
                                | FormFieldKind.RangedNumber(_, _, mn, mx, _) ->
                                    (if mn.IsSome then Some "min" else None), (if mx.IsSome then Some "max" else None)
                                | FormFieldKind.Range(_, _, mn, mx, _) ->
                                    (if mn.IsSome then Some "min" else None), (if mx.IsSome then Some "max" else None)
                                | FormFieldKind.Date(_, _, _, mn, mx, _) ->
                                    (if mn.IsSome then Some "min" else None), (if mx.IsSome then Some "max" else None)
                                | FormFieldKind.DateRange(_, _, _, mn, mx, _) ->
                                    (if mn.IsSome then Some "min" else None), (if mx.IsSome then Some "max" else None)
                                | _ -> None, None

                            let duplicated =
                                match cmp.Op with
                                | CompareOp.Gt
                                | CompareOp.Gte -> lower
                                | CompareOp.Lt
                                | CompareOp.Lte -> upper
                                | CompareOp.Eq
                                | CompareOp.Neq -> None

                            match duplicated with
                            | Some bound ->
                                defects.Add(
                                    PreEmitDefect.CompareDuplicatesBound(nodeIdStr, field.Id, control + "." + bound)
                                )
                            | None -> ()
                        | _ -> ()

                // An OMITTED value slot is always live: the Phase 596 auto-bind
                // gives the write-back default `$state.<field id>` to write to.
                let valueLive (value: Binding<'v> option) =
                    match value with
                    | None -> true
                    | Some b -> isWriteBackTarget b

                let inert =
                    match field.Kind with
                    | FormFieldKind.Text(value, oc) -> oc.IsNone && not (valueLive value)
                    | FormFieldKind.Number(value, oc) -> oc.IsNone && not (valueLive value)
                    | FormFieldKind.Checkbox(value, ot) -> ot.IsNone && not (valueLive value)
                    | FormFieldKind.Toggle(value, ot) -> ot.IsNone && not (valueLive value)
                    | FormFieldKind.Choice(_, value, oc) -> oc.IsNone && not (valueLive value)
                    | FormFieldKind.TextArea(value, oc, _) -> oc.IsNone && not (valueLive value)
                    | FormFieldKind.RangedNumber(value, oc, _, _, _) -> oc.IsNone && not (valueLive value)
                    | FormFieldKind.Range(value, oc, _, _, _) -> oc.IsNone && not (valueLive value)
                    | FormFieldKind.SegmentedChoice(_, value, oc, _) -> oc.IsNone && not (valueLive value)
                    | FormFieldKind.Date(value, oc, _, _, _, _) -> oc.IsNone && not (valueLive value)
                    | FormFieldKind.DateRange(value, oc, _, _, _, _) -> oc.IsNone && not (valueLive value)

                if inert then
                    defects.Add(PreEmitDefect.InertControl(nodeIdStr, sprintf "FormField(%s)" field.Id))

            spec.Fields |> List.iter checkField
        | NodeKind.Select spec ->
            let nodeIdStr = n.Id

            if spec.Multiple = Some true then
                let valuesLive =
                    match spec.Values with
                    | Some values -> isWriteBackTarget values
                    | None -> false

                if spec.OnChangeMulti.IsNone && not valuesLive then
                    defects.Add(PreEmitDefect.InertControl(nodeIdStr, "Select(multiple)"))
            elif spec.OnChange.IsNone && not (isWriteBackTarget spec.Value) then
                defects.Add(PreEmitDefect.InertControl(nodeIdStr, "Select"))
        | NodeKind.Filters _
        | NodeKind.Button _
        | NodeKind.FileUpload _ -> ()
        | NodeKind.Chart(spec) ->
            // FUARAN086–089 (Phase 640): schema-grounded chart validation. An
            // ungrounded field reference is the LANGUAGE's defect to catch
            // before lowering — a wrong field name otherwise lowers to a
            // silently flat/empty chart.
            let nodeIdStr = n.Id

            let kindName =
                match spec.Kind with
                | ChartKind.Line -> "Line"
                | ChartKind.Bar -> "Bar"
                | ChartKind.Area -> "Area"
                | ChartKind.Pie -> "Pie"
                | ChartKind.Scatter -> "Scatter"
                | ChartKind.Heatmap -> "Heatmap"

            // FUARAN088 — pie needs exactly one series (the 638 lowering
            // refuses multi-series geometry rather than truncating).
            (match spec.Kind with
             | ChartKind.Pie when spec.YFields.Length <> 1 ->
                 defects.Add(PreEmitDefect.ChartPieSeriesShape(nodeIdStr, spec.YFields.Length))
             | _ -> ())

            // FUARAN089 — Stacked is dead intent outside Bar/Area.
            (match spec.Kind with
             | ChartKind.Line
             | ChartKind.Scatter
             | ChartKind.Pie when spec.Stacked ->
                 defects.Add(PreEmitDefect.ChartStackedMeaningless(nodeIdStr, kindName))
             | _ -> ())

            // FUARAN086/087 — grounding, only where the schema is statically
            // known: an Embedded table with an EMPTY pipeline (a non-empty
            // pipeline changes the column set — Derive adds, Project/GroupBy
            // remove — and no static output-schema derivation exists yet, so
            // it deliberately passes ungrounded rather than false-positive).
            (match spec.Source with
             | Binding.Transform(TransformSource.Data(DataSource.Embedded table), [], _) ->
                 let colType (name: string) : ColumnType option =
                     table.Schema |> List.tryFind (fun (c, _) -> c = name) |> Option.map snd

                 let numeric (t: ColumnType) : bool =
                     match t with
                     | ColumnType.IntType
                     | ColumnType.FloatType
                     | ColumnType.BoolType -> true
                     | _ -> false

                 // FUARAN097 (Phase 882) — a temporal x-axis is a DECLARATION,
                 // and this is where the language grounds it. `date` and
                 // `timestamp` are both honoured (a timestamp's time-of-day is
                 // discarded by the lowering, which is a documented narrowing,
                 // not a mismatch); anything else cannot parse as a date, so
                 // every row's x would read as the epoch.
                 let temporalX =
                     match spec.XScale with
                     | Some ChartXScale.Temporal -> true
                     | _ -> false

                 let dated (t: ColumnType) : bool =
                     match t with
                     | ColumnType.DateType
                     | ColumnType.TimestampType -> true
                     | _ -> false

                 (match colType spec.XField with
                  | None -> defects.Add(PreEmitDefect.ChartFieldUngrounded(nodeIdStr, spec.XField))
                  | Some t ->
                      if temporalX && not (dated t) then
                          defects.Add(PreEmitDefect.ChartTemporalXNotDate(nodeIdStr, spec.XField, ColumnType.tag t))

                      // FUARAN087's x arm is NARROWED by a temporal declaration:
                      // a temporal Scatter reads its x as dates, so a date
                      // column there is correct rather than "not numeric", and
                      // FUARAN097 above is the rule that governs it. Without the
                      // narrowing a correctly-authored time-series scatter would
                      // raise a mismatch about the very column it declared.
                      match spec.Kind with
                      | ChartKind.Scatter when not (numeric t) && not temporalX ->
                          defects.Add(PreEmitDefect.ChartFieldTypeMismatch(nodeIdStr, spec.XField, ColumnType.tag t))
                      | _ -> ())

                 for yf in spec.YFields do
                     match colType yf with
                     | None -> defects.Add(PreEmitDefect.ChartFieldUngrounded(nodeIdStr, yf))
                     | Some t ->
                         if not (numeric t) then
                             defects.Add(PreEmitDefect.ChartFieldTypeMismatch(nodeIdStr, yf, ColumnType.tag t))
             | _ -> ())
        // The other visualisations are leaves with no pre-emit invariants yet.
        | NodeKind.Chart _
        | NodeKind.Map _ -> ()
        | NodeKind.Custom spec ->
            if spec.ModuleId = "" || spec.ComponentId = "" then
                defects.Add(PreEmitDefect.EmptyCustomKindIdentifier(spec.ModuleId, spec.ComponentId))

            customCheck n.Id spec.ModuleId spec.ComponentId spec.Props
            |> Option.iter defects.Add
        | NodeKind.ErrorBoundary spec ->
            // The boundary's `Child` + `Fallback`
            // subtrees both participate in the tree-wide NodeId uniqueness
            // check + empty-id surface. Nested boundaries are permitted —
            // each inner boundary's child + fallback walks normally. No
            // boundary-specific defect at v1 (the AI may legitimately emit
            // structurally identical child + fallback shapes during
            // exploratory authoring).
            walk spec.Child
            walk spec.Fallback
        | NodeKind.Switch spec ->
            let nodeIdStr = n.Id

            // FUARAN083 (Phase 392, widened by Phase 768): an empty-key State
            // selector is ungrounded — the switch can never resolve a case, so
            // it is stuck on `default`. Any other Binding is grounded by
            // construction (a Selection/Filter/Query names its source).
            match spec.On with
            | Binding.State("", _) -> defects.Add(PreEmitDefect.UngroundedSwitchStateKey nodeIdStr)
            | _ -> ()

            // FUARAN082 (Phase 392): duplicate `match` values make the later
            // case dead (first-match-wins). Report each duplicated value once.
            let seen = System.Collections.Generic.HashSet<string>()
            let reported = System.Collections.Generic.HashSet<string>()

            for c in spec.Cases do
                if not (seen.Add c.Match) && reported.Add c.Match then
                    defects.Add(PreEmitDefect.DuplicateSwitchMatch(nodeIdStr, c.Match))

            // The case children + default participate in the tree-wide NodeId
            // uniqueness + empty-id surface, so walk them all.
            spec.Cases |> List.iter (fun c -> walk c.Child)
            walk spec.Default
        | NodeKind.FragmentDecl spec ->
            // The decl's `Body` participates in the
            // tree-wide NodeId uniqueness check. Note that uniqueness here
            // is *pre-expansion* — at render time the renderer namespaces
            // interior ids by the ref's id, so the same body referenced by
            // two refs produces DOM-unique ids without an authoring
            // duplicate. Name-level uniqueness + unresolved/cyclic ref
            // checks are AST-walk concerns and live in the validator
            // (FUARAN056 / FUARAN057 / FUARAN058).
            walk spec.Body
        | NodeKind.FragmentRef _ -> ()
        // Mount (§4o) is an opaque isolation boundary — the guest interior is
        // a separate scope with its own id space, produced host-side by the
        // guest loader, so it is not walked into the host tree's NodeId
        // uniqueness check (same posture as FragmentRef). The mount node's own
        // id was already recorded via `recordNodeId n.Id`.
        | NodeKind.Mount _ -> ()

    walk node

    // Collect every id observed ≥ 2 times.
    for KeyValue(id, count) in nodeIdCounts do
        if count >= 2 then
            defects.Add(PreEmitDefect.DuplicateNodeId(id, count))

    // ── Cross-tree binding checks (Phase 427; the BindingWalk facts) ──
    // FUARAN070 / FUARAN071: every `Binding.Selection` read must target an
    // existing node (error), and that node should be a selection-producing
    // kind (warn) — `Binding.Selection` reaches parity with the declared-edge
    // checks the filter channel got in 421/424.
    let facts = BindingWalk.collect node

    for u in facts.Uses do
        match u.Use with
        | BindingWalk.BindingUse.Selection target ->
            match Map.tryFind target facts.Nodes with
            | None -> defects.Add(PreEmitDefect.DanglingSelection(u.Reader, target))
            | Some isProducer ->
                if not isProducer then
                    defects.Add(PreEmitDefect.SelectionOverNonProducer(u.Reader, target))
        | _ -> ()

    // FUARAN072 / FUARAN073: every wire-survivable `Action.Call` either
    // dispatches through a closure, or lands its response where a reader
    // looks (Phase 428).
    let readQueryNames =
        facts.Uses
        |> List.choose (fun u ->
            match u.Use with
            | BindingWalk.BindingUse.Query(name, _) -> Some name
            | _ -> None)
        |> Set.ofList

    for c in facts.Calls do
        match c.HasOnResult, c.Into with
        | false, None -> defects.Add(PreEmitDefect.CallResultDropped(c.Reader, c.Endpoint))
        | _, Some(CallResultTarget.Query name) when not (Set.contains name readQueryNames) ->
            defects.Add(PreEmitDefect.OrphanQueryFetch(c.Reader, name))
        | _ -> ()

    // ── The 421/424 filter consumption union (the consolidated deferral) ──
    // FUARAN074: a declared chip nothing consumes (a `Binding.Filter` read
    // outside the declaring chip, a `Query.dependsOn` reference, or a
    // `Transform` param source each count). FUARAN075: a DECLARED edge
    // (`dependsOn` / a param's Filter source) naming a filter no chip
    // declares. FUARAN076: a `params` entry the pipeline never references.
    let declaredFilterNames = facts.DeclaredFilters |> List.map snd |> Set.ofList

    for (ownerNodeId, name) in facts.DeclaredFilters do
        let consumed =
            facts.Uses
            |> List.exists (fun u ->
                match u.Use with
                | BindingWalk.BindingUse.Filter n -> n = name && u.Reader <> ownerNodeId
                | BindingWalk.BindingUse.TransformParamFilter n -> n = name
                | BindingWalk.BindingUse.Query(_, dependsOn) -> List.contains name dependsOn
                | _ -> false)

        if not consumed then
            defects.Add(PreEmitDefect.DecorativeFilter(ownerNodeId, name))

    for u in facts.Uses do
        match u.Use with
        | BindingWalk.BindingUse.TransformParamFilter n when not (Set.contains n declaredFilterNames) ->
            defects.Add(PreEmitDefect.DanglingFilterReference(u.Reader, n))
        | BindingWalk.BindingUse.Query(_, dependsOn) ->
            for n in dependsOn do
                if not (Set.contains n declaredFilterNames) then
                    defects.Add(PreEmitDefect.DanglingFilterReference(u.Reader, n))
        | BindingWalk.BindingUse.TransformParam(n, false) ->
            defects.Add(PreEmitDefect.UnreferencedTransformParam(u.Reader, n))
        | _ -> ()

    // FUARAN085 — two handler-free write-back writers on one state key.
    writeBackKeys
    |> Seq.groupBy (fun (key, _, _) -> key)
    |> Seq.iter (fun (key, writers) ->
        let ws = writers |> Seq.map (fun (_, nid, fid) -> nid, fid) |> List.ofSeq

        if ws.Length > 1 then
            defects.Add(PreEmitDefect.DuplicateWriteBackKey(key, ws)))

    // ── FUARAN098 — a `SetState` writing a key nothing reads (Phase 932) ──
    // 866's fake-affordance property, middle enforcement tier. The top tier is
    // structural (the renderer owns each admitted affordance, so its two ends
    // cannot be mis-paired) and the bottom is unenforceable and named as such
    // (`Notify` on a dead channel, `Invoke` of an unregistered capability, both
    // crossing the host boundary). This is the authored shape in between.
    //
    // The rule reasons from the ABSENCE of a read, so it stands down wherever
    // absence is not evidence: a `Binding.Computed` closure is handed the whole
    // state bag, and a registered `Custom` renderer is host code that may read
    // anything. Under either, "nothing reads this key" is unprovable rather than
    // false, and the fuaran-core#90 rule applies — refuse only what is PROVABLY
    // wrong.
    if not facts.StateKeys.OpaqueReader then
        let reported = System.Collections.Generic.HashSet<string>()

        for (writerNodeId, key) in facts.StateKeys.Writes do
            // Host-reserved keys are exempt through the Phase 782 guard's own
            // prefix rather than a second list beside it: a write there is
            // REFUSED at dispatch on every path, so its defect is that it is
            // unaddressable, not that it is unread.
            let unread = not (Set.contains key facts.StateKeys.Reads)

            if
                unread
                && not (StateKeyPolicy.isHostReserved key)
                && reported.Add(writerNodeId + "\u0000" + key)
            then
                defects.Add(PreEmitDefect.SetStateNoReader(writerNodeId, key))

    // ── FUARAN103 — a `Switch` selecting on a key nothing can write (Phase 768) ──
    // The read-side twin of the rule above, and the shape every emission in its
    // source cluster had. It reasons from the ABSENCE of a write, so it stands
    // down wherever absence is not evidence: any closure in the tree produces an
    // arbitrary action at dispatch time, a registered `Custom` renderer is host
    // code, and a `Mount` guest is a tree this walk never sees. Under any of
    // them the fuaran-core#90 rule applies — refuse only what is PROVABLY wrong.
    if not facts.StateKeys.OpaqueWriter then
        let reportedSwitch = System.Collections.Generic.HashSet<string>()

        for (switchNodeId, key) in facts.StateKeys.SwitchSelectors do
            // A host-reserved key (Phase 782) is the host's to write by
            // definition, so its absence from the tree's writers is expected
            // rather than a defect — the same exemption FUARAN098 takes, for
            // the mirror-image reason. An EMPTY key is FUARAN083's case, not
            // this one; reporting both would say the same thing twice.
            if
                key <> ""
                && not (Set.contains key facts.StateKeys.WriteKeys)
                && not (StateKeyPolicy.isHostReserved key)
                && reportedSwitch.Add(switchNodeId + " " + key)
            then
                defects.Add(PreEmitDefect.SwitchKeyNoWriter(switchNodeId, key))

    // ── FUARAN099 — a cross-field compare naming a key nothing can reach (Phase 864) ──
    //
    // The predicate's operand is a read, and a read of a key that no form field
    // owns and no writer in the tree sets is not a comparison that fails — it is
    // a comparison that never happens, on a field the author believes is
    // constrained.
    //
    // It reasons from an ABSENCE, so it takes the same stand-down FUARAN103
    // does: any closure in the tree writes arbitrary keys at dispatch time, so
    // under an opaque writer "nothing writes this" is unprovable rather than
    // false, and the fuaran-core#90 rule applies. `formOwnedStateKeys` is
    // deliberately tree-wide rather than per-form: a compare that reads a key
    // owned by a DIFFERENT form is unusual, not wrong, and refusing it here
    // would be the walk deciding a layout question.
    if not facts.StateKeys.OpaqueWriter then
        let reportedCompare = System.Collections.Generic.HashSet<string>()

        for (formNodeId, fieldId, key) in compareStateReads do
            if
                not (formOwnedStateKeys.Contains key)
                && not (Set.contains key facts.StateKeys.WriteKeys)
                && not (StateKeyPolicy.isHostReserved key)
                && reportedCompare.Add(formNodeId + " " + fieldId + " " + key)
            then
                defects.Add(PreEmitDefect.CompareKeyUnreachable(formNodeId, fieldId, key))

    if defects.Count = 0 then
        Ok()
    else
        Error(List.ofSeq defects)

/// Walk `node` (depth-first, pre-order) and surface every pre-emit defect.
/// Returns `Ok ()` on a clean tree; `Error defects` carries every defect
/// found (NOT short-circuited on the first one) so the AI can repair the
/// tree in a single turn rather than discovering defects one at a time.
let validate (node: Node<'Msg>) : Result<unit, PreEmitDefect list> =
    validateCore DecodePolicy.admitAll (fun _ _ _ _ -> None) node

/// `validate` + custom-prop schema enforcement (**FUARAN068**): every
/// `NodeKind.Custom` whose `(moduleId, componentId)` is registered has its
/// prop bag checked against the declared `PropSchema` via
/// `CustomRegistry.ValidateProps` — the shipped path that makes a registered
/// custom kind validated like a built-in, instead of a surface the host must
/// remember to call separately. An UNregistered custom kind passes untouched
/// (the registry only speaks for what it knows); a host with no registry
/// keeps calling the plain `validate`.
let validateWithRegistry (registry: CustomRegistry) (node: Node<'Msg>) : Result<unit, PreEmitDefect list> =
    validateCore
        DecodePolicy.admitAll
        (fun nodeId moduleId componentId props ->
            match registry.ValidateProps(moduleId, componentId, props) with
            | [] -> None
            | propDefects -> Some(PreEmitDefect.CustomPropSchemaViolation(nodeId, moduleId, componentId, propDefects)))
        node

/// `validate` + the **FUARAN104** kind-admission lint (Phase 1020): every node
/// whose wire discriminator falls outside `policy` is reported, so an authoring
/// host learns before emit that the deployment it names will refuse the tree.
///
/// **Advisory here; the decode boundary is the enforcement point.** A tree that
/// passes this has not been admitted by anything — it has merely not been
/// refused by a walk the emitter chose to run. The claim "this deployment's wire
/// boundary admits no escape hatches" is made by
/// `JsonDecode.decodeNodeWithPolicy`, on the receiving side, over bytes rather
/// than over a tree the same process built.
let validateWithPolicy (policy: DecodePolicy) (node: Node<'Msg>) : Result<unit, PreEmitDefect list> =
    validateCore policy (fun _ _ _ _ -> None) node

/// `validateWithRegistry` + the kind-admission lint — the both-declared form.
/// A profile that excludes only part of the guest boundary (`Mount` but not
/// `Custom`, say) still wants its registered custom kinds prop-checked, and
/// making the host pick one of the two walks would leave whichever it dropped
/// unrun.
let validateWithRegistryAndPolicy
    (registry: CustomRegistry)
    (policy: DecodePolicy)
    (node: Node<'Msg>)
    : Result<unit, PreEmitDefect list> =
    validateCore
        policy
        (fun nodeId moduleId componentId props ->
            match registry.ValidateProps(moduleId, componentId, props) with
            | [] -> None
            | propDefects -> Some(PreEmitDefect.CustomPropSchemaViolation(nodeId, moduleId, componentId, propDefects)))
        node
