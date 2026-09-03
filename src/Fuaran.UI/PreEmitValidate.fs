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
    /// **FUARAN105 (Warning)**. A `Binding.Transform` whose SOURCE is a
    /// `Binding.State` carrying no `defaultValue`, on a key nothing in the tree
    /// writes (Phase 865) — the silent zero. The Transform's initial snapshot
    /// table is derived from the source binding's own carried default, so a
    /// default-less source decodes to `TransformLive.emptySource`; with no
    /// writer the channel never changes, so a `groupBy`/`count` pipeline over it
    /// renders a plausible, permanent, wrong answer with nothing red anywhere.
    ///
    /// **A SIBLING reader's default IS a rescuer since Phase 1075, and the
    /// reversal is the point of that phase rather than a loosening of this
    /// rule.** Until the seeding rule landed, `Binding.State`'s `defaultValue`
    /// was a PER-READER FALLBACK: nothing wrote it into the store, so a grid
    /// declaring `State("rows", Some rows)` beside a badge reading
    /// `State("rows", None)` left the badge resolving from an unwritten slot,
    /// and this rule had to key on the Transform's OWN source or be silent on
    /// precisely the pair the charter was written about. Under seeding the
    /// grid's declaration seeds the slot, the badge reads it, and the rule
    /// widens to the charter §6 wording it was always meant to have: it fires
    /// where NO reader in the tree seeds the key and nothing writes it.
    /// Standing down on a sibling seed is now correct where it would have been
    /// wrong before; the go-red test that pinned the old reading pins the new
    /// one.
    ///
    /// **WARNING, and it stands down under any opaque writer**, for the same
    /// reason FUARAN103 does: a closure produces an arbitrary action at dispatch
    /// time and may write anything, and a host may populate the key directly.
    /// Host-reserved keys (the Phase 782 prefix) are exempt — the host's to
    /// write by definition.
    ///
    /// Carries the reading node's id and the key.
    | TransformSourceInert of nodeId: string * key: string
    /// **FUARAN106 (Error)**. Two `Binding.State` readers of ONE key declare
    /// DIFFERENT `defaultValue`s (Phase 1075) — the conflicting-seed defect the
    /// shared-data-source charter §3.4(1) named as the first rule the seeding
    /// semantics needs.
    ///
    /// Before seeding this was legal and harmless: each reader resolved its own
    /// fallback, and two disagreeing defaults simply meant two nodes showing
    /// two things. Under seeding there is ONE slot and the declarations are
    /// claims about it, so a disagreement is a fact about the document that
    /// cannot be resolved from the document. The renderer still has to render:
    /// it takes the FIRST declaration in walk order, which is deterministic and
    /// host-independent, and this rule is what stops that determinism from
    /// being a silent coin-toss the author never sees.
    ///
    /// **ERROR, where its two Phase-1075 siblings are Warnings, on the
    /// fuaran-core#90 rule.** Both declarations are in the tree; neither is a
    /// claim about a host the walk cannot see. The disagreement is decidable on
    /// the evidence the walk already holds — the same argument FUARAN099 makes
    /// for its own Error grade.
    ///
    /// **Two spellings of one table are NOT a disagreement.** A grid carries
    /// rows row-major and a Transform's live source carries the same data
    /// canonically columnar; comparing the raw values would call that a
    /// conflict and refuse the most idiomatic shape the pack teaches. Both
    /// sides are therefore normalised through the same transpose + columnar
    /// decode the decode-time snapshot uses before they are compared
    /// (`BindingWalk.seedFingerprint`).
    ///
    /// **What is NOT claimed.** The comparison is structural equality over the
    /// normalised form. A seed whose value is a LAZILY constructed sequence
    /// has no structural identity in .NET, so two such seeds carrying equal
    /// content compare unequal and are reported. That cannot arise on the wire
    /// — a decoded row feed is a materialised list and a decoded scalar is a
    /// primitive — so it is reachable only from a hand-authored tree that
    /// builds one default twice, where the report is at worst premature
    /// rather than wrong.
    ///
    /// Carries the key and the two declaring node ids, in walk order.
    | ConflictingStateSeeds of key: string * firstNodeId: string * secondNodeId: string
    /// **FUARAN107 (Warning)**. Two nodes carry STRUCTURALLY IDENTICAL inline
    /// tables (Phase 1075) — the two-inline-copies lint the shared-data-source
    /// charter asks for, and the rule that would have caught the emission the
    /// charter was written about: a `DataGrid` carrying its rows inline beside
    /// a `Badge` whose `Transform` carries its own separate copy of the same
    /// rows, with nothing anywhere saying the two are meant to be one table.
    ///
    /// **It is a Phase-1075 rule and not an older one because the REMEDY is
    /// what seeding creates.** Before the seeding rule the advice would have
    /// been to collapse the copies onto one declared name, and no such
    /// declaration existed — the Warning would have named a defect and pointed
    /// at nothing. Under seeding, one reader declares `defaultValue` on a state
    /// key and every other reader — a grid's `source`, a `Transform`'s live
    /// source — points at the key.
    ///
    /// **"Seedable" is read as re-expressible, and the widening is recorded
    /// rather than assumed.** Charter §6 words the row "where one is
    /// seedable". Every slot this rule collects from — a grid's or chart's
    /// `source`, a `Transform`'s source — can carry a `Binding.State`, so the
    /// qualifier selects nothing, and reading it narrowly would silence the
    /// rule on the sighted emission (whose grid carries a `Static` value).
    ///
    /// **Two readers of the SAME key are the shape this rule wants, not a
    /// defect.** A grid and a Transform both declaring the same rows under one
    /// key is sound, agrees under FUARAN106, and is what the pack teaches
    /// today; it is excluded rather than reported.
    ///
    /// **WARNING, because "identical" is not "meant to be one".** Two panels
    /// legitimately showing the same small reference table is unusual, not
    /// wrong, and the fuaran-core#90 rule refuses only what is provably wrong.
    /// Empty tables are excluded — an empty live source is what every
    /// unpopulated Transform decodes to, and pairing them would fire on trees
    /// carrying no inline data at all.
    ///
    /// Carries the two node ids in walk order, and the state key of the
    /// seedable copy when one of them has one.
    | DuplicateInlineTable of firstNodeId: string * secondNodeId: string * seedKey: string option
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

    /// **FUARAN108 (Error)**. A `Media` node whose `Label` resolves to nothing —
    /// an empty `Literal`, which is what `Defaults.media` carries so that the
    /// record can be constructed at all (Phase 1076).
    ///
    /// This is `ImageSpec.Alt`'s a11y floor WITHOUT the decorative escape, and
    /// the absence of that escape is the whole of the rule. An image can
    /// honestly declare `alt=""`: a spacer, a rule, a background texture adds
    /// nothing a screen-reader user needs, and the empty string is the way to
    /// say so. A media element is a TRANSPORT — a control a user can focus,
    /// play, pause and seek — so it is never decorative, and an unnamed one is
    /// announced as "video" or "audio" and nothing else. The reader is told a
    /// player exists and given no way to learn what it plays.
    ///
    /// Error rather than Warning because there is no legitimate shape it
    /// refuses. Every other reading of an empty label is a defect: an author
    /// who took `Defaults.media` and filled the source but not the name, an
    /// emitter that dropped a slot, a translation key that resolved to the
    /// empty string. A Warning would be advice about a document that cannot be
    /// used, which is what the pre-emit gate exists to stop.
    ///
    /// Carries the node's id.
    | MediaWithoutLabel of nodeId: string

    /// **FUARAN113 (Error)**. A `Media` node carrying a text track whose `Label`
    /// resolves to nothing - an empty or whitespace `Literal` (Phase 1110).
    ///
    /// FUARAN108's argument, one level down, and it survives the descent for a
    /// reason worth stating rather than assuming. A track's label is not
    /// decoration and not a caption: it is the ENTRY a user agent puts in its
    /// track menu, and it is the only thing distinguishing one track from
    /// another there. An unlabelled track is offered as its kind alone, so a
    /// reader choosing between two captions tracks - the ordinary case the
    /// moment a recording has a verbose and a plain cut - is shown two identical
    /// choices and given nothing to choose on.
    ///
    /// Error rather than Warning for FUARAN108's reason too: there is no
    /// legitimate shape it refuses. A track exists to be selected, so a track
    /// nobody can select is a defect under every reading - the wire already
    /// makes `label` required, and this rule catches the value that satisfies
    /// the requirement while meaning nothing.
    ///
    /// Only a LITERAL is judged. A `Bound` or `I18n` label resolves at render
    /// time from data this walk cannot see, so calling it empty would be a
    /// guess; the same restraint FUARAN108 shows one level up.
    ///
    /// **What this rule deliberately does NOT do, assessed and recorded per the
    /// phase's own instruction: it does not require a captions track on a
    /// `Video`.** The case for requiring one is strong in the abstract - WCAG
    /// 1.2.2 asks for captions on prerecorded video, and most video that reaches
    /// a reader is speech. The case against is that this validator cannot tell
    /// the difference between a lecture recording and a decorative silent loop
    /// behind a hero panel, and a rule that fires on the second is one authors
    /// learn to switch off - which costs the estate the rule on the first. There
    /// is nothing in the wire that distinguishes them: `loop` and `autoplay`
    /// hint at a decorative shape and are neither necessary nor sufficient for
    /// it. So the obligation stays where it can be stated truthfully - the
    /// spec's normative prose and an author's own gate - rather than being
    /// guessed at here. If a later phase adds a slot that DOES declare the
    /// distinction (a "decorative" or "silent" declaration, the `Image` alt
    /// precedent), the rule becomes decidable and should be revisited then.
    ///
    /// Carries the node's id and the track's index in the authored list, because
    /// "one of this node's tracks is unlabelled" is not an actionable message on
    /// a node with four of them.
    | TrackWithoutLabel of nodeId: string * trackIndex: int

    /// **FUARAN115 (Error)**. An `Embed` node whose `Title` resolves to nothing
    /// — an empty or whitespace `Literal` (Phase 1111).
    ///
    /// FUARAN108's argument on the kind next door, and it transfers exactly. A
    /// frame is not a picture: it is a focus container a reader tabs INTO, and
    /// one with no accessible name is announced as "frame" and nothing more, so
    /// a reader arriving in it is told that a frame exists and nothing about
    /// what is inside. There is no decorative embed, because there is no such
    /// thing as a decorative browsing context.
    ///
    /// Error rather than Warning for FUARAN108's reason: there is no legitimate
    /// shape it refuses. The wire already makes `title` required; this rule
    /// catches the value that satisfies the requirement while meaning nothing —
    /// which is exactly what `Defaults.embed` produces for an author who filled
    /// the source and forgot the name.
    ///
    /// Only a LITERAL is judged, on FUARAN108's restraint: a `Bound` or `I18n`
    /// title resolves at render time from data this walk cannot see.
    ///
    /// Carries the node's id.
    | EmbedWithoutTitle of nodeId: string

    /// **FUARAN116 (Warning)**. An `Embed` declaring BOTH `AllowScripts` and
    /// `AllowSameOrigin` (Phase 1111).
    ///
    /// The pair is the documented sandbox escape: a framed document holding its
    /// own origin AND script can reach the frame ELEMENT that frames it when
    /// that element is same-origin, and remove the `sandbox` attribute — after
    /// which a reload runs it unsandboxed. Every mediation this kind offers is
    /// then a mediation the framed document controls.
    ///
    /// **Warning, not Error, and the phase asked for the assessment either
    /// way.** The pair is also what every real cross-origin embed needs: a video
    /// player, a map and an embedded form each want script and their provider's
    /// own storage, and against a CROSS-origin frame the escape does not apply,
    /// because the framed document cannot reach an element in a document it has
    /// no access to. So an Error would refuse the ordinary case in order to
    /// catch the dangerous one, and a rule that refuses the ordinary case is one
    /// authors switch off — which costs the estate the rule on the case that
    /// mattered. What decides it is the origin relationship, and this walk
    /// cannot see it: `Src` is frequently a binding, and even a literal URL says
    /// nothing about the origin of the page that will frame it.
    ///
    /// **What this rule deliberately is NOT is an "all permissions" rule**,
    /// which is the shape the phase sketched. Two reasons, both decisive. The
    /// cardinality of "all" is an artefact of how many cases this enum happens
    /// to have, so the rule would change meaning every time a permission is
    /// admitted, without anybody deciding that it should. And it would MISS the
    /// two-case document that carries the actual hazard while firing on a
    /// four-case document that added fullscreen, which is inert. The named pair
    /// is the hazard; naming it is what makes the warning worth reading.
    ///
    /// Carries the node's id.
    | EmbedSandboxWeakened of nodeId: string

    /// **FUARAN118 (Warning)**. A node declaring `tooltip` whose hint resolves
    /// to nothing — an empty or whitespace `Literal` (Phase 1112).
    ///
    /// FUARAN111's argument, at the trait next door. A declared hint that says
    /// nothing is worse than no hint: the renderers emit no hint element for it
    /// (an empty box revealed on hover is not a thing to ship), so the author
    /// sees markup that silently did not appear, and the `aria-describedby` the
    /// declaration seemed to promise is not there either. Nothing anywhere else
    /// would ever say so — the whole failure is invisible from both sides,
    /// which is the accessibility family's founding reason for existing.
    ///
    /// **Warning, not Error, and the difference from FUARAN115 is the point.**
    /// An `Embed` with no title is refused because a frame with no name cannot
    /// be identified at all; a node with no hint is a node with no hint, which
    /// is the ordinary state of almost every node in every tree. What is wrong
    /// here is the DECLARATION, and the remedy is to write the sentence or drop
    /// the slot — neither of which is a refusal-grade defect in the document.
    ///
    /// Only a LITERAL is judged, on the family's standing restraint: a `Bound`
    /// or `I18n` hint resolves at render time from data this walk cannot see.
    ///
    /// Carries the node's id.
    | EmptyTooltipDeclaration of nodeId: string

    /// **FUARAN119 (Warning)**. A node carrying a `tooltip` while declaring
    /// `accessibility.hidden = true` (Phase 1112).
    ///
    /// `aria-hidden` removes the element and its whole subtree from the
    /// accessibility tree, so the hint's `aria-describedby` resolves to nothing
    /// a screen reader will ever read, and the hint element is hidden with it.
    /// What is left is a hover affordance for sighted pointer users on a node
    /// the author has declared is not part of the interface — which is either a
    /// hint nobody was meant to get, or a `hidden` nobody meant to set. Both
    /// readings are worth a line.
    ///
    /// **Warning, not Error.** A decorative node with a debugging hint is a
    /// legitimate shape, and this walk cannot tell it from the mistake. It is
    /// also frequently TRANSIENT — `hidden` is a `Binding<bool>`, so the same
    /// node is hidden in one state and shown in the next — which is exactly why
    /// only a `Binding.Static true` is judged: a bound `hidden` says nothing
    /// about the state the hint will be read in, and refusing it would fire on
    /// every node that is conditionally hidden, forever.
    ///
    /// Carries the node's id.
    | TooltipOnHiddenNode of nodeId: string

    /// **FUARAN120 (Warning)**. A `FormFieldKind.Combobox` whose option source
    /// is a STATIC and EMPTY list (Phase 1113) — nothing to suggest, and no
    /// dynamic source that could later supply anything.
    ///
    /// A typeahead over nothing is not a broken document; it is a control that
    /// renders, accepts focus, opens no listbox and can never complete. With
    /// `allowFreeText = true` it degrades to a plain text input the author did
    /// not ask for; with `allowFreeText = false` it is a control from which NO
    /// value is admissible, which is a field the reader cannot fill by any
    /// route. Neither reading is reported by anything else — the bytes are
    /// valid, every host renders them, and the emitter is told nothing.
    ///
    /// **Only a `Static` source is judged, on the family's standing restraint.**
    /// A `Query` / `State` / `Filter` / `Transform` source is exactly the
    /// asynchronous suggestion feed this control exists for, and it is empty at
    /// authoring time by construction. Judging it would fire on the shape the
    /// phase was built to enable, which is how a rule becomes one authors
    /// switch off.
    ///
    /// **Warning, not Error.** An empty static list is a legitimate transitional
    /// state — a form assembled before its options are known, a chip whose set
    /// is filled by a later edit — and refusing it would make an ordinary
    /// authoring step impossible. What is wrong is that nothing else would say
    /// so.
    ///
    /// Carries the node's id and the field / filter name.
    | ComboboxWithoutOptions of nodeId: string * fieldId: string

    /// **FUARAN121 (Warning)**. A `FileUpload` declaring `dropTarget` or
    /// `acceptPaste` while carrying NO `onSelect` handler (Phase 1115) — a
    /// gesture invited onto a control that consumes nothing.
    ///
    /// The picker degrades honestly without a handler: the user agent's own
    /// chrome still shows the chosen filename, so a reader can see the pick
    /// happened even when nothing downstream reads it. A drop zone and a paste
    /// target have no such fallback — the file vanishes on release with no
    /// user-agent feedback of any kind, which is a fake affordance in the sense
    /// the affordance→op charter's declines use the term: an invitation the
    /// document cannot honour.
    ///
    /// **The rule is about the GESTURE flags, not about the handler.** A plain
    /// handler-less upload is a legitimate authoring intermediate and is left
    /// alone; what this reports is the pairing. `AffordanceInertness` answers a
    /// different question — whether a PRESENT handler survived decode — so
    /// neither rule subsumes the other.
    ///
    /// **Warning, not Error.** A tree assembled before its handler is wired is
    /// an ordinary authoring step, and refusing it would make that step
    /// impossible. What is wrong is that nothing else would say so: the bytes
    /// are valid, every host renders them, and the reader is the one who finds
    /// out.
    ///
    /// Carries the node's id and the gesture(s) declared.
    | UploadGestureWithoutHandler of nodeId: string * gestures: string

    /// **FUARAN122 (Warning)**. A `Modal` node whose `Modality` is `Popover` and
    /// which is not anchored (Phase 1119) — either it declares no `Anchor` at
    /// all, or the `Anchor` names an id this tree does not carry.
    ///
    /// The two shapes are ONE defect because they have one consequence and one
    /// remedy: the renderer has no element to position against, so the popover
    /// leaves it in the document flow wherever the node sits — the static floor,
    /// and not the anchored surface the document asked for. The payload says
    /// which shape it is (`None` = undeclared, `Some id` =
    /// declared and dangling) so the message can name a typo when there is one,
    /// on `DanglingAccessibilityReference`'s precedent of one code carrying a
    /// discriminating slot.
    ///
    /// Judged against the SAME node universe as the dangling-`Selection` and
    /// dangling-accessibility-reference checks, so "a node in this tree" means
    /// one thing in this module — including where a walk does not cross (a
    /// `Mount` guest's interior is a separate id space, and an anchor into one
    /// is genuinely unreachable from the host tree).
    ///
    /// **Warning, not Error, and the split is the point.** A popover with a
    /// nonexistent anchor is a perfectly well-formed document: it decodes on
    /// every host, and no per-node decoder could judge it, because whether an id
    /// resolves is a fact about the WHOLE tree. Refusing it at decode would make
    /// a valid document unreadable to say something a validator says better.
    ///
    /// Carries the node's id and the declared anchor, if any.
    | PopoverWithoutAnchor of nodeId: string * declaredAnchor: string option

    /// **FUARAN123 (Warning)**. A `Modal` node with `Modality = Modal` carrying
    /// an `Anchor` (Phase 1119) — a dead declaration.
    ///
    /// Nothing reads it: a blocking dialog is positioned by the scrim, not by an
    /// element, so the id rides the wire, survives every round trip, and changes
    /// nothing on any host. The likely author intent is a popover, and saying so
    /// is the whole value of the rule — the alternative is a document that looks
    /// anchored, is not, and reports nothing.
    ///
    /// **Warning, not Error**: the declaration is inert, not harmful, and a tree
    /// mid-edit between the two modalities is an ordinary authoring step.
    ///
    /// Carries the node's id and the dead anchor.
    | AnchorOnBlockingModal of nodeId: string * anchor: string

    /// **FUARAN124 (Warning)**. A node declaring `style.direction` whose kind
    /// lays out no character data of its own and has no descendants to inherit
    /// it (Phase 1472) — a dead declaration.
    ///
    /// FUARAN123's shape at a different slot. A declared direction does two
    /// things and only two: it states which way a run of text reads, and it
    /// isolates that run from the bidirectional context around it. A glyph, a
    /// placeholder bar and a sparkline are none of those — there is no run, so
    /// the declaration rides the wire, survives every round trip, and changes
    /// nothing on any host. The likely author intent is the node that HOLDS the
    /// text, and saying so is the whole value of the rule.
    ///
    /// **Warning, not Error**: the declaration is inert, not harmful, and a
    /// tree mid-edit is an ordinary authoring step.
    ///
    /// **The set is deliberately NARROW, on the accessibility family's standing
    /// restraint — err towards silence.** Only kinds that lay out no character
    /// data at all AND carry no children are named. A container inherits `dir`
    /// to everything beneath it, so a direction there is live; a chart, a grid
    /// and a map lay out per-datum text through their own arms; a `Drawing`'s
    /// `Label` shape carries a `TextSource`; a `Custom` node's body is
    /// host-rendered and unknowable here. Every one of those passes ungrounded
    /// rather than risk a false accusation against a correct tree.
    ///
    /// Carries the node's id and its wire kind name.
    | DirectionOnTextlessNode of nodeId: string * kind: string

    /// **FUARAN125 (Warning)**. A Phase 1473 print-break declaration with
    /// nothing it can act on — a dead declaration, the FUARAN123 / FUARAN124
    /// shape at a third slot.
    ///
    /// TWO conditions share ONE code because they are one rule, stated at two
    /// slots: *a print-break declaration that names a boundary this node does
    /// not have*. `RepeatHeaderNoHeader` is a grid asking for its column
    /// headers to repeat on every page when it renders no header cells at all;
    /// `NoSubtreeToKeepTogether` is a container asking to stay whole when it
    /// renders no subtree that could straddle a boundary. In both, the
    /// declaration rides the wire, survives every round trip, and changes
    /// nothing on any host.
    ///
    /// **Warning, not Error**: an inert declaration is not harmful, and a tree
    /// whose columns arrive in a later authoring step is an ordinary
    /// mid-edit state.
    ///
    /// **The set is deliberately NARROW, on the accessibility family's
    /// standing restraint — err towards silence.** Only STATICALLY CERTAIN
    /// emptiness is reported: a `Box` that renders no children at all, and a
    /// grid whose header cells are empty on the leg it will actually render.
    /// Nothing here judges whether the rendering will in fact be paged, how
    /// long the content is, or whether a page boundary would have fallen
    /// inside this subtree — none of that is knowable pre-emit, and a
    /// declaration that merely turns out not to be needed is CORRECT
    /// authoring, not a defect.
    ///
    /// Carries the node's id and which condition fired.
    | DeadPrintBreak of nodeId: string * defect: PrintBreakDefect

    // ── The accessibility family (FUARAN109/110/111, Phase 727) ──────────────
    //
    // Until this family landed the runtime validator carried NO accessibility
    // rule at all. The posture was enforced at build time (the source-AST
    // walker's FUARAN040/041, which needs F# source and cannot run in a
    // browser) and audited at release time (the axe-over-DOM gate, which needs
    // a rendered DOM). Neither reaches the tree an AI emits or a navigator
    // edits, so every conformant host was left to re-derive the checks — and
    // one did, app-side, over decoded JSON, having to say in its own header
    // that it was borrowing no provenance it did not have.
    //
    // **Severity: WARNING for all three, deliberately** (the phase's recorded
    // decision). The rules run on every existing emission the moment they ship,
    // and a11y defects are overwhelmingly present in trees that are otherwise
    // correct — so shipping them at Error would turn a large body of working
    // emissions red on arrival, and a gate that is red on arrival is one people
    // learn to step over. Advisory first; a host or a release gate that wants
    // enforcement grades `describe`'s severity itself and decides to refuse.
    // Note what Warning does NOT mean here: `validate` still LISTS these in its
    // `Error` result, exactly as the module's existing Warning-severity rules
    // (FUARAN049, FUARAN069, FUARAN071, …) already are. Severity is the
    // projection's grade, not a second return channel — nothing in this family
    // changes the walker's contract.
    //
    // All three ERR TOWARDS SILENCE by construction. A defect they cannot
    // decide statically is not reported: a bound or i18n name resolves from
    // data no pre-emit walk can see, and calling it empty would be a guess. An
    // un-audited node is a cost this family can afford; a false accusation
    // against a correct tree is not.

    /// **FUARAN109 (Warning)**. An INTERACTIVE node that reaches a screen
    /// reader with no name: its structural naming slot is an empty literal and
    /// the node declares neither `Accessibility.Label` nor
    /// `Accessibility.LabelledBy`. Its accessible name would have to come from
    /// its own text content, and there is none.
    ///
    /// **Which kinds are interactive is READ from the language, not tabled
    /// here.** Every smart constructor passes a per-kind
    /// `Defaults.Accessibility.*` value, and this rule fires only where that
    /// value declares an ARIA role that names an OPERABLE element. So the
    /// language's own statement about a kind is the gate: give a kind a
    /// non-interactive default and it stops being audited in the same edit,
    /// with nothing here to update. The lock is deliberately ONE-DIRECTIONAL —
    /// a newly interactive kind whose naming slot is not wired below goes
    /// un-audited rather than falsely flagged.
    ///
    /// **The declared-name escape is not a loophole, and its hole is closed by
    /// FUARAN111.** A blank structural label on a node carrying
    /// `accessibility.label` is odd-looking and perfectly announced, so it is
    /// not flagged — that is the browser's own name computation (trait label →
    /// `aria-labelledby` target → element text content), not a softening. What
    /// would be a loophole is a DECLARED name that is itself empty, which would
    /// silence this rule while naming nothing; FUARAN111 catches exactly that
    /// shape, which is why the two ship together.
    ///
    /// Carries the node's id, its wire kind, and the naming slot that is empty.
    | InteractiveWithoutAccessibleName of nodeId: string * kind: string * slot: string

    /// **FUARAN110 (Warning)**. An `Accessibility.LabelledBy` /
    /// `DescribedBy` naming a node id the tree does not carry. The renderer
    /// emits the reference unconditionally, so the DOM gets an
    /// `aria-labelledby` pointing at nothing and the browser silently ignores
    /// it — the node is announced as though the reference had never been
    /// written, which is the worst of both: the author believes the element is
    /// named and no reader is told anything.
    ///
    /// This closes an asymmetry the app-side audit could only report: no op in
    /// the tree-op vocabulary reaches the `accessibility` trait (`UpdateProp`
    /// paths are rooted inside the kind spec), so a lens that finds a dangling
    /// ref cannot offer a fix for it. A pre-emit rule sees the whole tree
    /// BEFORE the emission is accepted, which is the one place the defect is
    /// actionable at all.
    ///
    /// Judged against the same node universe FUARAN070's dangling-`Selection`
    /// check uses, so "a node in this tree" means one thing in this module.
    ///
    /// Carries the referring node's id, the slot, and the missing target id.
    | DanglingAccessibilityReference of nodeId: string * slot: string * target: string

    /// **FUARAN111 (Warning)**. An accessibility slot the node DECLARES and
    /// leaves empty — `label` bound to a static empty (or valueless) string,
    /// or a `labelledBy` / `describedBy` naming the empty string.
    ///
    /// A declared-and-empty slot is worse than an absent one in both
    /// directions. Downwards: the renderer drops an empty `aria-label` (the
    /// projection filters it), so the author declared a name and the DOM got
    /// none, with nothing anywhere saying so. Upwards: a declared `label` is
    /// what tells FUARAN109 the node is named, so an empty one SILENCES the
    /// rule that would otherwise have caught the node — the defect suppresses
    /// its own detection. That is the whole reason this rule exists as a peer
    /// of 109 rather than a tidy extra.
    ///
    /// Only a `Binding.Static` label is judged, for the family's standing
    /// reason: any other binding resolves from data this walk cannot see.
    /// Whitespace counts as empty — a name of `" "` is not a name a listener
    /// can act on, and admitting it would make the rule evadable by a space,
    /// which is worse than not having it.
    ///
    /// Carries the node's id and the empty slot.
    | EmptyAccessibilityDeclaration of nodeId: string * slot: string

    /// **FUARAN112 (Warning)**. A closure-carrying action on a tree that may be
    /// bound for the wire — an `Action.Dispatch`, an `Action.Call` with an
    /// `onResult`, or an `Action.ReadFileBody` with an `onRead`. The canonical
    /// encoder emits the case's discriminator and DROPS the closure payload, and
    /// the decoder rebuilds it as the `"<closure>"` sentinel — so the
    /// interaction reaches a decoding host as a shape with no behaviour behind
    /// it: the button still renders, still fires, and does nothing.
    ///
    /// **This is the BACKSTOP, not the enforcement point.** The sanctioned path
    /// for a tree that must survive serialisation is
    /// `CanonicalJson.encodeNodeForTransport`, which REFUSES rather than warns —
    /// calling it is the author saying the interaction was meant to survive, and
    /// that is where intent is known. This rule exists for the author who
    /// reaches past that path, which is why it is a Warning: a walk cannot know
    /// whether the tree it is looking at will ever be encoded at all. An
    /// in-process Fable host renders `Dispatch` perfectly, forever, and telling
    /// it otherwise at Error severity would be wrong.
    ///
    /// **The typed answer is hole-binding, not this rule.** The remedy is not to
    /// delete the interaction: a browser raises a wire action (`Notify`, or
    /// `Call` with `into:`) and the host binds typed behaviour to the artifact's
    /// declared action holes. Full Fable is the one tier where `Dispatch`
    /// survives, because there the tree is never serialised.
    ///
    /// Reported per offending node and slot rather than once per tree — the
    /// author repairs each of them.
    ///
    /// Carries the node's id and the closure slot
    /// (`SlotCapability`'s `Type.slot` spelling).
    | WireLossyActionClosure of nodeId: string * slot: string
    /// **FUARAN114 (Error)**. A `DataGrid` names a column its own source cannot
    /// produce: a column `field`, or the grid's `rowKeyField`, absent from the
    /// statically-known schema of the `Binding.Transform` the grid reads
    /// (Phase 1149). The row projection resolves the name against each row and
    /// finds nothing, so the cell renders blank — or, for `rowKeyField`, every
    /// row keys off the same empty string and row identity silently collapses.
    /// A blank cell is indistinguishable from a legitimately empty value, which
    /// is why this is worth a code at all.
    ///
    /// **The read-side twin of FUARAN086**, which grounds a chart's field
    /// references against the same schema, over the same window, for the same
    /// reason. The window is the one the tier can DERIVE: a `Transform` over an
    /// `Embedded` table with an EMPTY pipeline. A non-empty pipeline changes the
    /// column set — `derive` adds, `project` and `groupBy` remove — and a `Ref`
    /// source, a `Query`, a `State` or a host `Static` row-seq is unknowable
    /// before the tree runs. All of those pass ungrounded, per the
    /// fuaran-core#90 rule: refuse only what is PROVABLY wrong. That is not a
    /// gap being tolerated; a rule that guessed here would fire on correct
    /// authoring, and an Error that is occasionally wrong gets suppressed.
    ///
    /// **Why this lives in the validator rather than a tier above it.** The
    /// alternative considered was hosting the check where a pipeline's output
    /// schema is already computable. It was declined: the chart's identical
    /// grounding rule lives here, and splitting one rule across two homes gives
    /// it two vocabularies and a code space that no longer says where a defect
    /// came from. The pipeline-bearing widening is a single call to the
    /// schema-walk `fuaran-core#112` shipped, at the one call site below, once
    /// this package's `Fuaran.Core.DataFrame` pin can name the version carrying
    /// it — the pins here are deliberately held behind what the public index
    /// serves, and this rule does not wait on that to be useful.
    ///
    /// Carries the grid node's id, the ungrounded field name, and the schema's
    /// column set — the author needs both halves to see whether it is a typo or
    /// the wrong source.
    | GridFieldUngrounded of nodeId: string * field: string * schemaColumns: string list

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

/// Why a print-break declaration cannot act (FUARAN125, Phase 1473). Typed
/// rather than a string for the same reason `SortDefect` is: the shapes stay
/// enumerable, and a third cannot be added by prose.
and [<RequireQualifiedAccess>] PrintBreakDefect =
    /// `repeatHeader: true` on a grid that renders no header cells on the leg
    /// it will take — no columns on the bound path, or empty `headers` on the
    /// static one. There is a header row group to repeat only when something
    /// is in it.
    | RepeatHeaderNoHeader
    /// `keepTogether` on a container that renders no subtree — an empty `Box`,
    /// or a `Separator`, whose emitted rule takes no children at all whatever
    /// the spec carries. There is nothing inside that could be split.
    ///
    /// **`breakBefore` is deliberately NOT reported at this shape**, and the
    /// asymmetry is the honest one: an empty box still generates a box, so a
    /// break BEFORE it is a live instruction to the formatter, where a break
    /// INSIDE it has nothing to act on. Reporting the pair together would have
    /// been tidier and wrong.
    | NoSubtreeToKeepTogether

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
    | PreEmitDefect.TransformSourceInert(nodeId, key) ->
        "FUARAN105",
        DefectSeverity.Warning,
        sprintf
            "'%s' derives from a Transform over state key '%s', but NOTHING in the tree seeds that key — no reader declares a defaultValue for it and nothing writes it — so the pipeline runs over an EMPTY table and renders a plausible wrong answer (a count of zero) that nothing reports; declare the rows once on any reader of the key ({\"$type\":\"State\",\"key\":\"%s\",\"defaultValue\":[…]}), which seeds the slot for every reader including this one, or give the key a writer. If the key is populated by the HOST, this warning is expected and can be ignored"
            nodeId
            key
            key
    | PreEmitDefect.ConflictingStateSeeds(key, firstNodeId, secondNodeId) ->
        "FUARAN106",
        DefectSeverity.Error,
        sprintf
            "state key '%s' is seeded twice with DIFFERENT values — '%s' and '%s' each declare a defaultValue for it, and a key has one slot, so only the first declaration ('%s') takes effect and the second is silently discarded; declare the value ONCE and let the other reader carry {\"$type\":\"State\",\"key\":\"%s\"} with no defaultValue, or give the two readers different keys if they are genuinely different data"
            key
            firstNodeId
            secondNodeId
            firstNodeId
            key
    | PreEmitDefect.DuplicateInlineTable(firstNodeId, secondNodeId, seedKey) ->
        "FUARAN107",
        DefectSeverity.Warning,
        sprintf
            "'%s' and '%s' each carry their own inline copy of the SAME table — the two copies can silently diverge, and nothing in the tree says they are meant to be one source; declare the rows once under a state key (%s) and have the other read {\"$type\":\"State\",\"key\":\"<key>\"} with no defaultValue, which resolves to the seeded slot. If the two are genuinely independent data that happen to match, this warning is expected and can be ignored"
            firstNodeId
            secondNodeId
            (match seedKey with
             | Some k -> sprintf "'%s' already declares one" k
             | None -> "neither declares one yet")
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
    | PreEmitDefect.MediaWithoutLabel nodeId ->
        "FUARAN108",
        DefectSeverity.Error,
        sprintf
            "media node '%s' has an EMPTY label — a media element is a transport, not a picture, so it is never decorative and there is no honest empty case the way there is for an image's alt; without a name it is announced to a screen reader as \"video\" or \"audio\" and nothing more, telling the reader that a player exists and not what it plays. Give 'label' the text a listener needs to decide whether to play it"
            nodeId
    | PreEmitDefect.TrackWithoutLabel(nodeId, trackIndex) ->
        "FUARAN113",
        DefectSeverity.Error,
        sprintf
            "media node '%s' carries a text track at index %d with an EMPTY label - a track's label IS its entry in the user agent's track menu, and it is the only thing that tells one track from another there, so an unlabelled one is offered as its kind alone and a reader choosing between two captions tracks is shown two identical choices. Give the track's 'label' the text a reader needs to pick it"
            nodeId
            trackIndex
    | PreEmitDefect.EmbedWithoutTitle nodeId ->
        "FUARAN115",
        DefectSeverity.Error,
        sprintf
            "embed node '%s' has an EMPTY title — a frame is a focus container a reader tabs into, not a picture, so it is never decorative; without a name it is announced to a screen reader as \"frame\" and nothing more, telling the reader that something is embedded and not what. Give 'title' the text a reader needs to decide whether to enter it"
            nodeId
    | PreEmitDefect.EmbedSandboxWeakened nodeId ->
        "FUARAN116",
        DefectSeverity.Warning,
        sprintf
            "embed node '%s' declares both AllowScripts and AllowSameOrigin — against a SAME-ORIGIN document that pair is the documented sandbox escape, because the framed document can then reach its own frame element and remove the sandbox attribute. It is also what every real cross-origin embed needs, and nothing in this tree says which this is, so this is a warning rather than a refusal: confirm the source is a third-party origin, or drop AllowSameOrigin if the provider does not need its own storage"
            nodeId
    | PreEmitDefect.EmptyTooltipDeclaration nodeId ->
        "FUARAN118",
        DefectSeverity.Warning,
        sprintf
            "node '%s' declares a tooltip and leaves it EMPTY — a hint that hints nothing. The renderers emit no hint element for an empty one, so the markup you expected is silently absent and so is the aria-describedby that would have carried it to a screen reader; write the sentence the reader needs, or drop the slot"
            nodeId
    | PreEmitDefect.TooltipOnHiddenNode nodeId ->
        "FUARAN119",
        DefectSeverity.Warning,
        sprintf
            "node '%s' carries a tooltip while declaring accessibility.hidden = true — aria-hidden removes the node and its whole subtree from the accessibility tree, taking the hint and its aria-describedby with it, so what is left is a hover affordance for sighted pointer users on a node declared not to be part of the interface. Drop the hint, or drop the hidden declaration if the node was meant to be announced"
            nodeId
    | PreEmitDefect.ComboboxWithoutOptions(nodeId, fieldId) ->
        "FUARAN120",
        DefectSeverity.Warning,
        sprintf
            "combobox '%s' on node '%s' declares a STATIC and EMPTY option list — a typeahead with nothing to suggest, and no dynamic source that could supply anything later. It renders, it takes focus, and it opens no listbox: with allowFreeText it is a plain text input you did not ask for, and without it no value is admissible at all. Give the options a Query / State source if the suggestions arrive at runtime, list them if they are known, or use a Text field if free text is what you meant"
            fieldId
            nodeId
    | PreEmitDefect.UploadGestureWithoutHandler(nodeId, gestures) ->
        "FUARAN121",
        DefectSeverity.Warning,
        sprintf
            "file upload '%s' declares %s and carries no onSelect handler — the gesture is invited and consumes nothing. A picker at least leaves the chosen filename in the user agent's own chrome; a dropped or pasted file disappears on release with no feedback at all, so the reader is told the upload worked and it did not. Wire onSelect, or drop the gesture declaration until it is wired"
            nodeId
            gestures
    | PreEmitDefect.PopoverWithoutAnchor(nodeId, declaredAnchor) ->
        "FUARAN122",
        DefectSeverity.Warning,
        (match declaredAnchor with
         | None ->
             sprintf
                 "popover '%s' declares no anchor — a Popover is positioned against the node it was opened from, and with nothing to position against the renderer leaves it in the document flow wherever the node happens to sit, which is the static floor and not the surface you asked for. Set anchor to the id of the control that opens it, or use modality Modal if a blocking dialog is what you meant"
                 nodeId
         | Some target ->
             sprintf
                 "popover '%s' declares anchor = '%s', which is not a node in this tree — the anchor resolves to no element, so the popover is left in the document flow exactly as an undeclared one is, and the declaration reads as honoured when it was not. Point it at a node that exists (a dangling anchor is usually a typo or a node that has since moved), or drop the declaration and use modality Modal if a blocking dialog is what you meant"
                 nodeId
                 target)
    | PreEmitDefect.AnchorOnBlockingModal(nodeId, anchor) ->
        "FUARAN123",
        DefectSeverity.Warning,
        sprintf
            "modal '%s' declares anchor = '%s' while its modality is Modal — a dead declaration. A blocking dialog is positioned by its scrim and not by an element, so the id rides the wire, survives every round trip and changes nothing on any host. Set modality to Popover if an anchored surface is what you meant, or drop the anchor"
            nodeId
            anchor
    | PreEmitDefect.DirectionOnTextlessNode(nodeId, kind) ->
        "FUARAN124",
        DefectSeverity.Warning,
        sprintf
            "node '%s' declares style.direction while its kind is %s — a dead declaration. A direction states which way a run of text reads and isolates it from the bidirectional context around it, and this kind lays out no text and holds no children to inherit it, so the declaration rides the wire, survives every round trip and changes nothing on any host. Move it to the node that carries the text, or drop it"
            nodeId
            kind
    | PreEmitDefect.DeadPrintBreak(nodeId, PrintBreakDefect.RepeatHeaderNoHeader) ->
        "FUARAN125",
        DefectSeverity.Warning,
        sprintf
            "grid '%s' declares repeatHeader while it renders no header cells — a dead declaration. Repeating a header at the top of every page needs a header row group with something in it, so this rides the wire, survives every round trip and changes nothing on any host. Give the grid its columns (or, on the static leg, its headers), or drop the declaration"
            nodeId
    | PreEmitDefect.DeadPrintBreak(nodeId, PrintBreakDefect.NoSubtreeToKeepTogether) ->
        "FUARAN125",
        DefectSeverity.Warning,
        sprintf
            "node '%s' declares keepTogether while it renders no subtree — a dead declaration. Keeping a subtree whole across a page boundary needs a subtree that could straddle one, and this container has no rendered children, so the declaration rides the wire, survives every round trip and changes nothing on any host. Move it to the container that holds the content, or drop it"
            nodeId
    | PreEmitDefect.InteractiveWithoutAccessibleName(nodeId, kind, slot) ->
        "FUARAN109",
        DefectSeverity.Warning,
        sprintf
            "%s '%s' reaches a screen reader with no name — '%s' is empty and the node declares neither accessibility.label nor accessibility.labelledBy, so its accessible name would have to come from its text content and there is none; give '%s' the text a listener needs, or name the element with accessibility.label"
            kind
            nodeId
            slot
            slot
    | PreEmitDefect.DanglingAccessibilityReference(nodeId, slot, target) ->
        "FUARAN110",
        DefectSeverity.Warning,
        sprintf
            "node '%s' declares accessibility.%s = '%s', which is not a node in this tree — the emitted %s points at nothing and the browser ignores it, so the element is announced as though the reference had never been written; point it at a node that exists, or drop the slot and name the element with accessibility.label"
            nodeId
            slot
            target
            // The attribute name, spelled out rather than lower-cased at
            // runtime: `Fuaran.UI` is ASCII-only and culture-free by policy
            // (see the FUARAN102 scanner's note), and the pair is closed. The
            // fallback is not dead code — the generated defect vocabulary
            // renders every message from a SENTINEL slot value, and a two-arm
            // `if` would have it claim `aria-describedby` for a slot named
            // `<slot>`. Echoing the slot renders a shape rather than a wrong
            // claim, which is what that artefact is for.
            (match slot with
             | "labelledBy" -> "aria-labelledby"
             | "describedBy" -> "aria-describedby"
             | other -> "aria-" + other)
    | PreEmitDefect.EmptyAccessibilityDeclaration(nodeId, slot) ->
        "FUARAN111",
        DefectSeverity.Warning,
        sprintf
            "node '%s' declares accessibility.%s and leaves it EMPTY — a declared name that names nothing, which the renderer drops rather than emits, and which additionally silences the missing-name check that would otherwise have caught this node; give the slot real text, or remove it so the element's own content supplies the name"
            nodeId
            slot
    | PreEmitDefect.WireLossyActionClosure(nodeId, slot) ->
        "FUARAN112",
        DefectSeverity.Warning,
        sprintf
            "node '%s' carries a host closure in '%s' — the canonical encoder drops the payload and the decoder rebuilds it as \"<closure>\", so a decoding host receives an affordance that fires and does nothing; replace it with a wire-representable action (Action.Notify, or Action.Call with into:) and bind the typed behaviour host-side to the artifact's declared action hole. Encode with encodeNodeForTransport to have this refused rather than warned. If this tree is rendered IN PROCESS and never serialised, the closure is correct and this warning is expected"
            nodeId
            slot
    | PreEmitDefect.GridFieldUngrounded(nodeId, field, schemaColumns) ->
        "FUARAN114",
        DefectSeverity.Error,
        sprintf
            "grid '%s' names field '%s', absent from its source's statically-known schema [%s] — the row projection resolves it against nothing, so the cell renders blank (or, for rowKeyField, every row shares one empty key and row identity collapses); fix the name, or source the grid from data that carries the column (Phase 1149)"
            nodeId
            field
            (String.concat ", " schemaColumns)

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

// ── FUARAN109/110/111 — the accessibility family (Phase 727) ─────────────────
//
// The derivation, in one place. The rules themselves are three lines each; what
// is worth reading is WHERE their inputs come from, because the family's whole
// claim is that it re-derives nothing.
//
//  · WHICH kinds are interactive — read from `Defaults.Accessibility.*`, the
//    per-kind trait every smart constructor in `Fuaran.fs` passes into
//    `buildNode`. The language states this; this module consults it. An
//    app-side audit had to guess at the same question from a hand-written list
//    with a source-lock test behind it, because it could not reach the
//    defaults. Sitting inside `Fuaran.UI`, downstream of `Defaults.fs`, this
//    one can, so the list is gone and the values are the input.
//  · WHAT counts as an operable role — an EXHAUSTIVE match over `AriaRole`, so
//    a role added to the DU does not compile until someone decides. That is the
//    lock in the direction that matters: the vocabulary cannot grow past the
//    rule silently.
//  · WHAT the accessible name is — the browser's own computation, in the
//    browser's own order: the declared trait label, then an `aria-labelledby`
//    target, then the element's text content. Not "what the renderer emits":
//    `renderButton` puts `ButtonSpec.Label` in the button's TEXT CONTENT and
//    injects no `aria-label`, so a node with a filled structural label and no
//    trait at all is correctly named and must not be flagged.
//
// The one-directional lock is stated once here and holds for all three: a kind
// the language stops calling interactive stops being audited; a kind that
// BECOMES interactive without a naming slot wired into `interactiveNaming`
// below goes un-audited. Un-audited is the failure this family can afford.

/// An `AriaRole` naming an element the user OPERATES — the roles for which
/// "reaches a screen reader with no name" is a defect rather than an
/// observation. Exhaustive on purpose: a role added to the DU will not compile
/// here until it is classified.
let private isInteractiveRole (role: AriaRole) : bool =
    match role with
    | AriaRole.Button
    | AriaRole.Link
    | AriaRole.Form
    | AriaRole.Tab -> true
    | AriaRole.Custom raw ->
        // `Custom` is an open string, and only one widget role is reached by
        // any default the language ships (`Select`'s combobox). The rest of the
        // space is deliberately NOT judged: guessing at an arbitrary role would
        // accuse a node of a defect the language never declared, and this
        // family errs towards silence.
        raw = "combobox"
    | AriaRole.Dialog
    | AriaRole.Alert
    | AriaRole.Status
    | AriaRole.Banner
    | AriaRole.Navigation
    | AriaRole.Main
    | AriaRole.Region
    | AriaRole.Heading
    | AriaRole.Progressbar
    | AriaRole.Tablist
    | AriaRole.Tabpanel -> false

/// Whether the language's OWN per-kind accessibility default declares the kind
/// interactive. This is the gate on FUARAN109: the `Defaults.Accessibility.*`
/// value is the input, never a restatement of it.
let private defaultDeclaresInteractive (dflt: Accessibility option) : bool =
    match dflt with
    | None -> false
    | Some a ->
        match a.Role with
        | Some role -> isInteractiveRole role
        | None -> false

/// A `TextSource` statically known to render nothing. Only a LITERAL is judged,
/// exactly as FUARAN108 judges `Media.Label`: a `Bound` or `I18n` source
/// resolves at render time from data this walk cannot see, so calling it empty
/// would be a guess. Whitespace counts as empty.
let private isEmptyTextSource (t: TextSource) : bool =
    match t with
    | TextSource.Literal s -> s.Trim() = ""
    | TextSource.Bound _
    | TextSource.I18n _ -> false

/// Does this kind lay out NO character data of its own AND hold no children to
/// inherit a declared direction (Phase 1472, FUARAN124)?
///
/// A closed allow-list of three rather than an exhaustive match, deliberately —
/// the accessibility family's standing restraint applied to a new slot. The
/// DEFAULT answer is "say nothing", which is always safe: an unreported dead
/// declaration costs a document one inert key, where a false accusation against
/// a correct tree costs the rule its credibility. A kind that later becomes
/// genuinely textless is therefore silent until somebody adds it here, which is
/// the direction this list is allowed to be wrong in.
let private isTextlessLeaf (kind: NodeKind<'Msg>) : bool =
    match kind with
    // A glyph. `Icon.label` is `aria-label` — attribute text, not laid out —
    // which is `Image.Alt`'s reading in the bidi policy next door.
    | NodeKind.Icon _
    // Placeholder bars: no characters, by definition.
    | NodeKind.Skeleton _
    // A shape drawn from numbers. Unlike `Drawing`, whose `Label` shape carries
    // a `TextSource`, a sparkline has no text vocabulary at all.
    | NodeKind.Sparkline _ -> true
    | _ -> false

/// A `Binding<string>` statically known to carry nothing — a static empty (or
/// whitespace) string, or a `Static` slot with no value at all. Every other
/// binding resolves at runtime and is not judged.
let private isEmptyStaticText (binding: Binding<string>) : bool =
    match binding with
    | Binding.Static None -> true
    | Binding.Static(Some s) -> s.Trim() = ""
    | _ -> false

/// Whether the trait DECLARES a name for the node. Tested on the DECLARATION,
/// not on the emission: a bound label resolves to nothing in a pre-emit walk
/// and is still a name, and the build-time rule makes the same concession in
/// the same place ("trust the binding to produce a non-empty string at
/// runtime"). An empty declaration is FUARAN111's finding, not this one's.
let private declaresAccessibleName (a11y: Accessibility option) : bool =
    match a11y with
    | None -> false
    | Some a -> a.Label.IsSome || a.LabelledBy.IsSome

/// For a kind the language pairs with an interactive accessibility default: the
/// default itself, the structural slot whose text NAMES the element, and that
/// slot's wire name. `None` for every other kind.
///
/// This is the family's only per-kind association, and it is the naming SLOT
/// rather than the interactivity verdict — the verdict comes from the default
/// above. The slots are each their kind's required naming field:
/// `submitLabel` names a form (through its submit button); `label` names the
/// other three.
let private interactiveNaming (kind: NodeKind<'Msg>) : (Accessibility option * TextSource * string) option =
    match kind with
    | NodeKind.Button spec -> Some(Defaults.Accessibility.button, spec.Label, "label")
    | NodeKind.Select spec -> Some(Defaults.Accessibility.select, spec.Label, "label")
    | NodeKind.Form spec -> Some(Defaults.Accessibility.form, spec.SubmitLabel, "submitLabel")
    | NodeKind.FileUpload spec -> Some(Defaults.Accessibility.fileUpload, spec.Label, "label")
    | _ -> None

/// The per-node half of the family: FUARAN109 (an unnamed interactive element)
/// and FUARAN111 (a declared-and-empty slot). FUARAN110 needs the whole tree
/// and is judged post-walk.
let private accessibilityDefects (n: Node<'Msg>) : PreEmitDefect list =
    let unnamed =
        match interactiveNaming n.Kind with
        | Some(dflt, naming, slot) when
            defaultDeclaresInteractive dflt
            && isEmptyTextSource naming
            && not (declaresAccessibleName n.Accessibility)
            ->
            [ PreEmitDefect.InteractiveWithoutAccessibleName(n.Id, wireKindName n.Kind, slot) ]
        | _ -> []

    let declaredEmpty =
        match n.Accessibility with
        | None -> []
        | Some a ->
            let emptyRef (slot: string) (target: string option) =
                match target with
                | Some s when s.Trim() = "" -> Some(PreEmitDefect.EmptyAccessibilityDeclaration(n.Id, slot))
                | _ -> None

            [ (match a.Label with
               | Some b when isEmptyStaticText b -> Some(PreEmitDefect.EmptyAccessibilityDeclaration(n.Id, "label"))
               | _ -> None)
              emptyRef "labelledBy" a.LabelledBy
              emptyRef "describedBy" a.DescribedBy ]
            |> List.choose id

    // Phase 1112 — the node-level tooltip trait's two rules. They live in this
    // function rather than a walk of their own because they are the same family
    // of finding: a declaration that reaches assistive technology as nothing,
    // with no visible output anywhere to say so.
    let tooltip =
        match n.Tooltip with
        | None -> []
        | Some hint ->
            let empty =
                if isEmptyTextSource hint then
                    [ PreEmitDefect.EmptyTooltipDeclaration n.Id ]
                else
                    []

            let hidden =
                match n.Accessibility |> Option.bind _.Hidden with
                | Some(Binding.Static(Some true)) -> [ PreEmitDefect.TooltipOnHiddenNode n.Id ]
                | _ -> []

            empty @ hidden

    // Phase 1472 — the declared-direction trait's one rule, here for the same
    // reason the tooltip pair is: it is a property of the node envelope rather
    // than of any kind's spec, so it is decided once for every kind instead of
    // in forty-one arms.
    let direction =
        match n.Style with
        | Some style when style.Direction <> TextDirection.Auto && isTextlessLeaf n.Kind ->
            [ PreEmitDefect.DirectionOnTextlessNode(n.Id, wireKindName n.Kind) ]
        | _ -> []

    unnamed @ declaredEmpty @ tooltip @ direction

/// The `(slot, target)` accessibility references a node declares. Empty targets
/// are excluded — an empty slot is FUARAN111's finding, and reporting the same
/// value twice under two codes would be noise rather than coverage.
let private accessibilityRefs (n: Node<'Msg>) : (string * string) list =
    match n.Accessibility with
    | None -> []
    | Some a ->
        let namedRef (slot: string) (target: string option) =
            match target with
            | Some s when s.Trim() <> "" -> Some(slot, s)
            | _ -> None

        [ namedRef "labelledBy" a.LabelledBy; namedRef "describedBy" a.DescribedBy ]
        |> List.choose id

let private validateCore
    (policy: DecodePolicy)
    // Phase 577 (FUARAN112) — whether this tree is declared bound for the wire.
    // The closure rule is decidable from the tree alone, but its RELEVANCE is
    // not: an in-process Fable host renders `Action.Dispatch` perfectly and
    // forever, so a walk that reported it unconditionally would be accusing the
    // idiomatic F# shape of a defect it does not have. The caller declares the
    // intent by choosing `validateForTransport`, exactly as it declares a
    // deployment by choosing `validateWithPolicy`.
    (forTransport: bool)
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
    // Phase 727 (FUARAN110) — (readerNodeId, slot, target) per declared
    // accessibility reference. Collected here and judged post-walk for the same
    // reason FUARAN070's dangling `Selection` is: "names a node in this tree"
    // is only answerable once the whole tree has been seen.
    let accessibilityRefUses = ResizeArray<string * string * string>()
    // Phase 1119 (FUARAN122) — (popoverNodeId, declaredAnchor) per `Popover`
    // whose anchor must be resolved against the whole tree. Collected here and
    // judged post-walk for the same reason the accessibility references are:
    // whether an id exists is not a per-node fact.
    let popoverAnchorUses = ResizeArray<string * string>()

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

    /// FUARAN120 (Phase 1113) — a combobox whose option source is a STATIC and
    /// EMPTY list. One helper because a filter chip carries the same control as
    /// a form field since the 0.2.0 unification, and one rule spelt twice is one
    /// rule that will eventually differ.
    ///
    /// Only `Static` is judged: every other binding case names a source resolved
    /// at render time, and a suggestion feed that is empty at authoring time is
    /// this control's whole purpose. `Static None` counts as empty for the
    /// reason `Static (Some [])` does — both say "no options, and none coming".
    let comboboxWithoutOptions (nodeId: string) (fieldId: string) (kind: FormFieldKind<'Msg>) =
        match kind with
        | FormFieldKind.Combobox(_, _, Binding.Static None, _) ->
            defects.Add(PreEmitDefect.ComboboxWithoutOptions(nodeId, fieldId))
        | FormFieldKind.Combobox(_, _, Binding.Static(Some []), _) ->
            defects.Add(PreEmitDefect.ComboboxWithoutOptions(nodeId, fieldId))
        | _ -> ()

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

        // FUARAN109 / FUARAN111 (Phase 727) — the per-node half of the
        // accessibility family. Sited here rather than in the per-kind arms
        // below because the trait it reads lives on the NODE, not in any kind
        // spec: one site covers every kind at once, and a kind the language
        // newly declares interactive is reached with no arm to remember.
        accessibilityDefects n |> List.iter defects.Add

        // FUARAN110's evidence — judged after the walk, see the declaration.
        for (slot, target) in accessibilityRefs n do
            accessibilityRefUses.Add(n.Id, slot, target)

        // Per-kind: check kind-specific invariants + enumerate children.
        match n.Kind with
        // -- Layout --
        | NodeKind.Box spec ->
            // FUARAN125 (Phase 1473) — `keepTogether` on a container that
            // renders no subtree. Two shapes reach it and only two: a box whose
            // children are empty, and the `Separator` role, whose emitted rule
            // takes no children at all whatever the spec carries.
            //
            // `breakBefore` is NOT judged here: an empty box still generates a
            // box, so a break before it remains a live instruction. Nothing here
            // judges whether the rendering will be paged or whether a boundary
            // would have fallen in this subtree either — neither is knowable
            // pre-emit, and a declaration that turns out not to be needed is
            // correct authoring, not a defect.
            if
                spec.KeepTogether
                && (List.isEmpty spec.Children || spec.Role = BoxRole.Separator)
            then
                defects.Add(PreEmitDefect.DeadPrintBreak(n.Id, PrintBreakDefect.NoSubtreeToKeepTogether))

            spec.Children |> List.iter walk
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

            // FUARAN122 / FUARAN123 (Phase 1119) — the anchor declaration read
            // against the modality. An UNDECLARED anchor on a popover is
            // answerable here; a DECLARED one is deferred to the post-walk pass,
            // where the tree's whole id universe is known.
            (match spec.Modality, spec.Anchor with
             | ModalityKind.Popover, None -> defects.Add(PreEmitDefect.PopoverWithoutAnchor(nodeIdStr, None))
             | ModalityKind.Popover, Some target -> popoverAnchorUses.Add(nodeIdStr, target)
             | ModalityKind.Modal, Some target -> defects.Add(PreEmitDefect.AnchorOnBlockingModal(nodeIdStr, target))
             | ModalityKind.Modal, None -> ())

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

            // FUARAN125 (Phase 1473) — `repeatHeader` on a grid that renders no
            // header cells. The two legs have different header sources and the
            // rule asks the one this grid will actually take: `staticRows`
            // carries its own `headers`, and the bound path derives the header
            // row from `columns`.
            //
            // `keepRowsTogether` gets no companion rule, deliberately. Whether a
            // grid has rows is a property of its resolved SOURCE, which is a
            // runtime fact for every binding shape but the embedded one — so the
            // rule would fire on a correct grid whose rows simply had not
            // arrived. That is the false accusation the restraint exists to
            // avoid, and an empty grid is a legitimate mid-edit tree.
            if spec.RepeatHeader then
                let rendersNoHeader =
                    match spec.StaticRows with
                    | Some sr -> List.isEmpty sr.Headers
                    | None -> List.isEmpty spec.Columns

                if rendersNoHeader then
                    defects.Add(PreEmitDefect.DeadPrintBreak(nodeIdStr, PrintBreakDefect.RepeatHeaderNoHeader))

            // FUARAN114 (Phase 1149): a declared field the grid's own source
            // cannot produce. FUARAN077 above asks whether a column names
            // ANYTHING; this asks whether what it names is THERE — the read-side
            // twin of FUARAN086, over the same window and by the same restraint.
            //
            // The window is what this tier can derive: an `Embedded` table with
            // an EMPTY pipeline. A non-empty pipeline changes the column set
            // (derive adds, project/groupBy remove) and every other source shape
            // is unknowable pre-emit, so both pass ungrounded rather than
            // false-positive. `fuaran-core#112` shipped the pipeline walk into
            // the dataframe package this file already consumes: widening this
            // rule is replacing the `[]` pattern below with that call, once the
            // pin here can name the version carrying it.
            (match spec.Source with
             | Binding.Transform(TransformSource.Data(DataSource.Embedded table), [], _) ->
                 let schemaColumns = table.Schema |> List.map fst

                 let ground (field: string) =
                     if not (List.contains field schemaColumns) then
                         defects.Add(PreEmitDefect.GridFieldUngrounded(nodeIdStr, field, schemaColumns))

                 // Reported per offending name rather than once per grid: a grid
                 // pointed at the wrong source names several missing columns, and
                 // the author repairs each of them.
                 for col in spec.Columns do
                     col.Field |> Option.iter ground

                 spec.RowKeyField |> Option.iter ground
             | _ -> ())

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
        // FUARAN108 (Phase 1076): a media transport with no accessible name.
        //
        // Only a LITERAL is judged. A `Bound` or `I18n` label resolves at render
        // time from data this walk cannot see, so calling it empty would be a
        // guess — the same restraint FUARAN092 shows for a bound `href` two arms
        // below. What is left is the case that is decidable and is also the case
        // that actually happens: `Defaults.media` carries the empty literal so
        // the record can be constructed, and an author who fills `Src` and
        // forgets `Label` ships exactly this.
        //
        // Whitespace counts as empty. A label of `" "` is not a name a listener
        // can act on, and admitting it would make the rule trivially evadable by
        // a space — which is worse than not having the rule, because the
        // document would then carry a green gate saying it had been checked.
        | NodeKind.Media spec ->
            match spec.Label with
            | TextSource.Literal s when s.Trim() = "" -> defects.Add(PreEmitDefect.MediaWithoutLabel n.Id)
            | _ -> ()

            // FUARAN113 (Phase 1110): the same judgement, per track. Reported
            // INDEPENDENTLY of FUARAN108 rather than short-circuiting on it - a
            // node can carry both defects, and a walk that reported only the
            // node-level one would send an author back for a second pass after
            // fixing it.
            spec.Tracks
            |> List.iteri (fun i t ->
                match t.Label with
                | TextSource.Literal s when s.Trim() = "" -> defects.Add(PreEmitDefect.TrackWithoutLabel(n.Id, i))
                | _ -> ())
        // FUARAN115 / FUARAN116 (Phase 1111): the embed's two rules, reported
        // independently of each other for FUARAN113's reason — a node can carry
        // both, and a walk that stopped at the first would send an author back
        // for a second pass. Whitespace counts as empty on FUARAN108's argument:
        // a title of `" "` is not a name a reader can act on, and admitting it
        // would make the rule evadable by a space, which is worse than not
        // having it because the document would then carry a green gate.
        | NodeKind.Embed spec ->
            match spec.Title with
            | TextSource.Literal s when s.Trim() = "" -> defects.Add(PreEmitDefect.EmbedWithoutTitle n.Id)
            | _ -> ()

            if
                List.contains EmbedPermission.AllowScripts spec.Permissions
                && List.contains EmbedPermission.AllowSameOrigin spec.Permissions
            then
                defects.Add(PreEmitDefect.EmbedSandboxWeakened n.Id)
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
                 | FormFieldKind.DateRange(value, oc, _, _, _, _) -> recordWriteBack value oc.IsNone
                 | FormFieldKind.Combobox(_, oc, _, value) -> recordWriteBack value oc.IsNone)

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
                 | FormFieldKind.DateRange(value, _, _, _, _, _) -> recordOwnedKey value
                 | FormFieldKind.Combobox(_, _, _, value) -> recordOwnedKey value)

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
                        // Phase 1113 — the combobox is a choice-shaped control,
                        // so it honours neither the text bounds nor `format`,
                        // exactly as `Choice` does. `allowFreeText` does NOT
                        // change that: what the reader types is still a
                        // selection expressed by typing, and a rule asking for
                        // an email format on a suggestion list is the confusion
                        // this table exists to name.
                        | FormFieldKind.Combobox _ -> "Combobox", false, false

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
                    | FormFieldKind.Combobox(_, oc, _, value) -> oc.IsNone && not (valueLive value)

                comboboxWithoutOptions nodeIdStr field.Id field.Kind

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
        | NodeKind.Filters spec ->
            // Phase 1113 — a filter chip carries an ordinary `FormFieldKind`
            // since the 0.2.0 unification, so an empty static combobox is the
            // same defect here as on a form field and is reported by the same
            // rule. Split out of the no-op group below for that one check; the
            // rest of a Filters node is still walked elsewhere.
            spec.Items
            |> List.iter (fun item -> comboboxWithoutOptions n.Id item.Name item.Kind)
        | NodeKind.FileUpload spec ->
            // FUARAN121 (Phase 1115) — a declared ingress gesture with nothing
            // to consume it. Split out of the no-op group for this one check.
            if spec.OnSelect.IsNone && (spec.DropTarget || spec.AcceptPaste) then
                let gestures =
                    match spec.DropTarget, spec.AcceptPaste with
                    | true, true -> "dropTarget and acceptPaste"
                    | true, false -> "dropTarget"
                    | _ -> "acceptPaste"

                defects.Add(PreEmitDefect.UploadGestureWithoutHandler(n.Id, gestures))
        | NodeKind.Button _ -> ()
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

    // FUARAN110 (Phase 727) — an accessibility reference naming a node the tree
    // does not carry. Judged against `facts.Nodes`, the SAME node universe the
    // dangling-`Selection` check immediately above uses, so "a node in this
    // tree" means one thing in this module rather than two — notably it agrees
    // about the boundaries a walk does not cross (a `Mount` guest's interior is
    // a separate id space, and a reference into one is genuinely dangling from
    // the host tree's point of view).
    for (readerId, slot, target) in accessibilityRefUses do
        if not (Map.containsKey target facts.Nodes) then
            defects.Add(PreEmitDefect.DanglingAccessibilityReference(readerId, slot, target))

    // FUARAN122 (Phase 1119) — a popover anchored at an id the tree does not
    // carry. Judged against `facts.Nodes`, the same node universe the two checks
    // above use, so an anchor and an `aria-describedby` agree about what "a node
    // in this tree" means. This is the half a decoder structurally cannot do:
    // `ModalSpec.anchor` admits any string on the wire precisely because
    // resolving it is a whole-tree question, and that split is recorded in the
    // decoder beside the field.
    for (popoverId, target) in popoverAnchorUses do
        if not (Map.containsKey target facts.Nodes) then
            defects.Add(PreEmitDefect.PopoverWithoutAnchor(popoverId, Some target))

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

    // ── FUARAN105 — a Transform over an unfillable State source (Phase 865) ──
    //
    // The silent zero. `Binding.State`'s `defaultValue` is a per-reader
    // FALLBACK, not a slot seed (`BindingResolver.fs`), so a Transform whose own
    // source declares no default resolves from an unwritten slot: the decoder's
    // initial snapshot is `TransformLive.emptySource`, and a `groupBy`/`count`
    // over it renders zero — correct-looking, permanent, and reported by
    // nothing. The shape is exactly the one the shared-data-source charter
    // sighted: a grid carrying the rows on its own `defaultValue` beside a badge
    // deriving from the same key without one.
    //
    // It reasons from the ABSENCE of a write, so it takes the same stand-down
    // FUARAN103 does — an opaque writer makes "nothing writes this" unprovable
    // rather than false, and a host-reserved key is the host's to write.
    //
    // **A sibling reader's default IS consulted since Phase 1075.** Under the
    // seeding rule a declaration anywhere in the tree fills the slot for every
    // reader, so the rule widens to charter §6's wording — it fires where NO
    // reader seeds the key. 865 could not read it that way, because under the
    // per-reader fallback a sibling's default never reached the Transform and
    // standing down on one would have silenced the rule on exactly the pair the
    // charter was written about.
    if not facts.StateKeys.OpaqueWriter then
        let reportedTransform = System.Collections.Generic.HashSet<string>()

        // An EMPTY declaration is not a rescuer: `defaultValue: []` leaves the
        // slot exactly as unseeded, which is the silent zero this rule names.
        let seededKeys =
            facts.StateKeys.Seeds
            |> List.filter (fun (d: BindingWalk.StateSeedDecl) -> not (BindingWalk.isEmptySeed d.Fingerprint))
            |> List.map (fun d -> d.Key)
            |> Set.ofList

        for (readerNodeId, key) in facts.StateKeys.TransformInertSources do
            // An EMPTY key names no slot at all; it is a malformed source rather
            // than an unfillable one, and reporting it here would say nothing
            // the author can act on.
            if
                key <> ""
                && not (Set.contains key seededKeys)
                && not (Set.contains key facts.StateKeys.WriteKeys)
                && not (StateKeyPolicy.isHostReserved key)
                && reportedTransform.Add(readerNodeId + " " + key)
            then
                defects.Add(PreEmitDefect.TransformSourceInert(readerNodeId, key))

    // ── FUARAN106 — two declarations of one seeded slot (Phase 1075) ──
    //
    // Decidable from the tree ALONE: both declarations are in hand, and a key
    // has one slot. Runs unconditionally — no opaque-writer stand-down, because
    // the rule reasons about a PRESENCE (two disagreeing declarations) rather
    // than an absence, so nothing a host might do later makes the disagreement
    // stop being one.
    //
    // A host-reserved key is exempt for the same reason it is everywhere else:
    // the seeding pass refuses to seed one (Phase 782), so two declarations
    // there conflict over a slot neither can fill, which is a different defect
    // and not this one.
    let seedsByKey =
        facts.StateKeys.Seeds
        |> List.filter (fun (d: BindingWalk.StateSeedDecl) ->
            d.Key <> ""
            && not (StateKeyPolicy.isHostReserved d.Key)
            // `defaultValue: []` declares nothing — it is the value an unseeded
            // slot already has, and today it is also the only way a Transform's
            // source slot can spell "I read this key and carry no data of my
            // own". Reporting it as a disagreement would raise an Error on
            // exactly the document the seeding rule exists to make work.
            && not (BindingWalk.isEmptySeed d.Fingerprint))
        |> List.groupBy (fun d -> d.Key)

    for (key, decls) in seedsByKey do
        match decls with
        | first :: rest ->
            // Only the FIRST disagreement is reported per key: the remedy is to
            // declare the value once, so listing every later reader would repeat
            // one instruction n times.
            match rest |> List.tryFind (fun d -> d.Fingerprint <> first.Fingerprint) with
            | Some conflicting ->
                defects.Add(PreEmitDefect.ConflictingStateSeeds(key, first.Reader, conflicting.Reader))
            | None -> ()
        | [] -> ()

    // ── FUARAN107 — two inline copies of one table (Phase 1075) ──
    //
    // The charter's two-copies lint. Pairs are reported once per (earlier,
    // later) node pair, and two entries that share a state key are the SHARING
    // this phase exists to make possible rather than a duplication.
    let inlineTables = facts.StateKeys.InlineTables

    if not (List.isEmpty inlineTables) then
        let reportedPair = System.Collections.Generic.HashSet<string>()
        let indexed = inlineTables |> List.indexed

        for (i, a) in indexed do
            for (j, b) in indexed do
                if
                    j > i
                    && a.Reader <> b.Reader
                    && a.Table = b.Table
                    // One shared key is one source, however many readers point
                    // at it — the shape the seeding rule creates.
                    && not (a.SeedKey.IsSome && a.SeedKey = b.SeedKey)
                    && reportedPair.Add(a.Reader + " " + b.Reader)
                then
                    let seedKey =
                        match a.SeedKey, b.SeedKey with
                        | Some k, _ -> Some k
                        | _, Some k -> Some k
                        | None, None -> None

                    defects.Add(PreEmitDefect.DuplicateInlineTable(a.Reader, b.Reader, seedKey))

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

    // ── FUARAN112 — a closure-carrying action on a tree bound for the wire ──
    //
    // Runs only under `forTransport`, for the reason given at the parameter.
    // The facts come from the ONE walk `BindingWalk.collect` already performs
    // over every action slot, so a new closure-bearing `Action` case is
    // classified there (exhaustively, no wildcard) rather than escaping here.
    //
    // Deduplicated on (node, slot): a `Chain` holding two `Dispatch`es is one
    // repair on one node, and reporting it twice would tell the author there
    // are two things wrong with it.
    if forTransport then
        let reportedClosure = System.Collections.Generic.HashSet<string>()

        for (c: BindingWalk.ClosureUse) in facts.Closures do
            if reportedClosure.Add(c.Reader + " " + c.Slot) then
                defects.Add(PreEmitDefect.WireLossyActionClosure(c.Reader, c.Slot))

    if defects.Count = 0 then
        Ok()
    else
        Error(List.ofSeq defects)

/// Walk `node` (depth-first, pre-order) and surface every pre-emit defect.
/// Returns `Ok ()` on a clean tree; `Error defects` carries every defect
/// found (NOT short-circuited on the first one) so the AI can repair the
/// tree in a single turn rather than discovering defects one at a time.
let validate (node: Node<'Msg>) : Result<unit, PreEmitDefect list> =
    validateCore DecodePolicy.admitAll false (fun _ _ _ _ -> None) node

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
        false
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
    validateCore policy false (fun _ _ _ _ -> None) node

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
        false
        (fun nodeId moduleId componentId props ->
            match registry.ValidateProps(moduleId, componentId, props) with
            | [] -> None
            | propDefects -> Some(PreEmitDefect.CustomPropSchemaViolation(nodeId, moduleId, componentId, propDefects)))
        node

/// `validate` + the **FUARAN112** wire-lossy-closure lint (Phase 577): every
/// `Action.Dispatch`, `Action.Call(onResult = Some _)` and
/// `Action.ReadFileBody(onRead = Some _)` in the tree is reported, because each
/// carries host code the canonical encoder renders as `"<closure>"`.
///
/// **Call this when the tree is bound for the wire, and only then.** The flag is
/// not a strictness dial: it is the caller stating that the tree will be
/// serialised, which is the fact the rule needs and cannot derive. A tree
/// rendered in process by the Fable host keeps its closures and is correct;
/// `validate` says nothing about them, deliberately.
///
/// **Advisory here; the refusal lives with the encoder.**
/// `CanonicalJson.encodeNodeForTransport` returns `Error` on exactly this set,
/// at the moment of encoding, which is where the loss would actually happen. A
/// tree that passes this has not been proved wire-faithful — it has merely not
/// been refused by a walk the author chose to run.
let validateForTransport (node: Node<'Msg>) : Result<unit, PreEmitDefect list> =
    validateCore DecodePolicy.admitAll true (fun _ _ _ _ -> None) node
