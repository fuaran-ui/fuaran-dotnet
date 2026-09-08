module Fuaran.UI.Renderer.AgGridPlan

// ============================================================================
//  Fuaran — what the AG Grid adapter does with the DECLARATIVE grid vocabulary
//  (Phase 1611)
//
//  Six declarative grid behaviours reached the wire between Phases 861 and
//  1125 — `defaultSort` / `sortStateKey` (861), `pageSize` / `pageStateKey`
//  (862), `editStateKey` (863), `reorderable` (934), `transferInKey` /
//  `transferOutKey` (1123) and `exportable` (1125) — and the first-party grid
//  honours all of them. The AG Grid adapter read NONE of them: sort and filter
//  were hard-coded on per column, and the other five names did not appear in
//  [AgGridAdapter.fs](AgGridAdapter.fs) at all. So the same decoded tree
//  behaved differently on the two grid backends, and which behaviour a reader
//  got depended only on whether the host happened to have wired an adapter.
//
//  That is the failure class [VisAdapter.fs](VisAdapter.fs) already records
//  twice — `NodeId` (Phase 427's selection default) and `EgressPolicy` (Phase
//  1523's destination floor) were each STRUCTURALLY unreachable on the adapter
//  path while working on the first-party one — arriving a third time, one axis
//  over.
//
//  ── Why the decisions are PURE, and live here rather than in the adapter ──
//  The same reason `GridExport`, `GridTransfer`, `GridPaste` and `SwitchStage`
//  are pure. [AgGridAdapter.fs](AgGridAdapter.fs) is Fable-only by
//  construction: its `import`s reach npm packages and its output is a React
//  element, so the .NET test runner can neither mount it nor call into it.
//  A parity claim written inside it could only ever be ASSERTED — and a phase
//  whose whole subject is six fields that were INERT must not ship a test that
//  passes because the field was never exercised.
//
//  So every judgement the adapter makes about a declared field is made HERE,
//  over `FSharp.Core` and the shipped `BindingResolver` predicates, and the
//  adapter's job is reduced to applying the result to AG's props. The parity
//  test then runs the corpus's grid fixtures through `disposition` on BOTH
//  backends and can fail.
//
//  ── The two dispositions, and why there is no third ──────────────────────
//  A declared field is either ACTED ON by a backend or it is not, and where it
//  is not, the reason is part of the answer. `Inert` carries that reason, and
//  the adapter reports it by name at render time through
//  [Diagnostics.fs](Diagnostics.fs) — which is the whole difference between a
//  named refusal and a silent drop, and the acceptance this phase is held to.
//
//  `Inert` is deliberately NOT "the adapter is worse here". Both backends
//  report it for the shipped rules that decline a declaration — an
//  `editStateKey` over a `Transform` source has no writable destination on
//  EITHER path, and Phase 863's `editDestination` is the one function that says
//  so. Where the two disagree, the disagreement is the finding.
// ============================================================================

open Fuaran.Core
open Fuaran.UI.Types

/// One declarative grid-behaviour field, named as the WIRE names it.
///
/// Per-column narrowings (`sortable`, `editable`) are behaviours in their own
/// right rather than details of their grid-level parents: Phase 861/863's rule
/// is that a column flag NARROWS, and a backend that honoured the grid-level
/// key while ignoring the column's `false` would be honouring the wrong
/// declaration — which is exactly what the adapter did, with `sortable` boxed
/// `true` on every column it emitted.
[<RequireQualifiedAccess>]
type Behaviour =
    | DefaultSort
    | SortStateKey
    | ColumnSortable
    | PageSize
    | PageStateKey
    | EditStateKey
    | ColumnEditable
    | Reorderable
    | TransferInKey
    | TransferOutKey
    | Exportable

/// The wire spelling. Used in the adapter's render-time refusal lines and in
/// the parity test's failure messages, so a reader meeting either is told the
/// name they would grep the tree for.
let name (behaviour: Behaviour) : string =
    match behaviour with
    | Behaviour.DefaultSort -> "defaultSort"
    | Behaviour.SortStateKey -> "sortStateKey"
    | Behaviour.ColumnSortable -> "columns[].sortable"
    | Behaviour.PageSize -> "pageSize"
    | Behaviour.PageStateKey -> "pageStateKey"
    | Behaviour.EditStateKey -> "editStateKey"
    | Behaviour.ColumnEditable -> "columns[].editable"
    | Behaviour.Reorderable -> "reorderable"
    | Behaviour.TransferInKey -> "transferInKey"
    | Behaviour.TransferOutKey -> "transferOutKey"
    | Behaviour.Exportable -> "exportable"

/// Every behaviour, in wire-declaration order. The parity test quantifies over
/// this rather than over a list of its own, so a seventh grid behaviour reaching
/// the wire without an adapter answer is a FAILING test rather than an
/// unexamined gap — which is the only durable form the acceptance can take.
let all: Behaviour list =
    [ Behaviour.DefaultSort
      Behaviour.SortStateKey
      Behaviour.ColumnSortable
      Behaviour.PageSize
      Behaviour.PageStateKey
      Behaviour.EditStateKey
      Behaviour.ColumnEditable
      Behaviour.Reorderable
      Behaviour.TransferInKey
      Behaviour.TransferOutKey
      Behaviour.Exportable ]

/// What a backend does with one declared field.
[<RequireQualifiedAccess>]
type Disposition =
    /// The spec does not declare it. Computed from the spec alone, so both
    /// backends necessarily agree — which is what makes a disagreement
    /// anywhere else a real finding rather than a spelling difference.
    | NotDeclared
    /// Declared, and this backend acts on it.
    | Honoured
    /// Declared, and this backend does NOT act on it. The reason is carried
    /// rather than described elsewhere: an unreasoned `Inert` is the silent
    /// drop wearing a type.
    | Inert of reason: string

/// Which backend a disposition is about. Only used to make the parity test's
/// failure messages say which side declined.
[<RequireQualifiedAccess>]
type Backend =
    | FirstParty
    | AgGrid

// ─── Shipped predicates, in one place ───────────────────────────────────────
//
// Every rule below reaches the SAME function the first-party renderer calls, so
// the two answers cannot drift: `editDestination` (863), `reorderDestination`
// (934) and `sourceHostPagesOn` (862) are the shipped rules, and this module
// adds none of its own.

/// Phase 862's pagination precondition, stated once: both members, and a
/// positive page size. `Render.fs` spells it as a `when` guard on the pair; a
/// backend that read one without the other would page by a size nothing
/// requested, or request a page of no size.
let paginates (spec: GridSpec<'Msg>) : bool =
    match spec.PageStateKey, spec.PageSize with
    | Some _, Some size -> size > 0
    | _ -> false

/// Does this grid's source page HOST-side? Phase 862's source-shape rule — a
/// `Query` whose `dependsOn` names the page key hands back the page itself.
let hostPages (spec: GridSpec<'Msg>) : bool =
    match spec.PageStateKey with
    | Some key -> BindingResolver.sourceHostPagesOn spec.Source key
    | None -> false

/// Does any column narrow `sortable`?
let private declaresColumnSortable (spec: GridSpec<'Msg>) =
    spec.Columns |> List.exists (fun c -> c.Sortable.IsSome)

/// Does any column narrow `editable`?
let private declaresColumnEditable (spec: GridSpec<'Msg>) =
    spec.Columns |> List.exists (fun c -> c.Editable.IsSome)

/// The reason BOTH backends decline an edit declaration with no writable
/// destination — Phase 863's rule, not a backend's opinion.
[<Literal>]
let noEditDestination =
    "no writable destination: the grid is not editable, or it declares no editStateKey over a source that has a writable slot, so an edit would be a gesture with nowhere to commit"

[<Literal>]
let noReorderDestination =
    "no writable destination: a reorder commits the whole rows value, and this grid declares no editStateKey over a writable source"

[<Literal>]
let incompletePagination =
    "pagination needs BOTH pageSize and pageStateKey: a page size with no key drives nothing, and a key with no size has no page to slice"

/// The AG-side refusal for a host-paged grid. Named rather than generic
/// because the remedy is specific: AG's client-side row model derives its pager
/// from the rows it was handed, so over a host page it would report a one-page
/// total — the fake-affordance-by-understatement the declared-total ruling
/// refuses. The community build's other row models are a different data
/// contract entirely, not a switch.
[<Literal>]
let agHostPagedPagination =
    "host-paged source (a Query whose dependsOn names the page key): AG's client-side row model derives its pager from the rows it holds, so it would report a one-page total over a host's page. The first-party grid honours it with a count-less pager"

/// The AG-side refusal for cross-container transfer. `addRowDropZone` is in the
/// community build, but registering one grid as another's drop zone needs a
/// handle to the sibling — and the adapter is called once per grid, holds no
/// registry and cannot see its siblings. Building one would be a second
/// coordination mechanism beside `GridTransfer`'s, which is what the shared-key
/// design exists to avoid.
[<Literal>]
let agNoTransfer =
    "cross-grid transfer needs a drop-zone registry across grid instances, and the adapter is invoked once per grid with no handle to its siblings. The first-party grid honours it through the shared transfer key"

/// What the FIRST-PARTY grid does with one declared field.
let firstPartyDisposition (sources: BindingResolver.BindingSources) (spec: GridSpec<'Msg>) (behaviour: Behaviour) =
    ignore sources

    match behaviour with
    | Behaviour.DefaultSort ->
        if spec.DefaultSort.IsSome then
            Disposition.Honoured
        else
            Disposition.NotDeclared
    | Behaviour.SortStateKey ->
        if spec.SortStateKey.IsSome then
            Disposition.Honoured
        else
            Disposition.NotDeclared
    | Behaviour.ColumnSortable ->
        if declaresColumnSortable spec then
            Disposition.Honoured
        else
            Disposition.NotDeclared
    | Behaviour.PageSize ->
        if spec.PageSize.IsNone then Disposition.NotDeclared
        elif paginates spec then Disposition.Honoured
        else Disposition.Inert incompletePagination
    | Behaviour.PageStateKey ->
        if spec.PageStateKey.IsNone then Disposition.NotDeclared
        elif paginates spec then Disposition.Honoured
        else Disposition.Inert incompletePagination
    | Behaviour.EditStateKey ->
        if spec.EditStateKey.IsNone then
            Disposition.NotDeclared
        elif (BindingResolver.editDestination spec.Editable spec.EditStateKey spec.Source).IsSome then
            Disposition.Honoured
        else
            Disposition.Inert noEditDestination
    | Behaviour.ColumnEditable ->
        if declaresColumnEditable spec then
            Disposition.Honoured
        else
            Disposition.NotDeclared
    | Behaviour.Reorderable ->
        if not spec.Reorderable then
            Disposition.NotDeclared
        elif (BindingResolver.reorderDestination spec.Reorderable spec.EditStateKey spec.Source).IsSome then
            Disposition.Honoured
        else
            Disposition.Inert noReorderDestination
    | Behaviour.TransferInKey ->
        if spec.TransferInKey.IsSome then
            Disposition.Honoured
        else
            Disposition.NotDeclared
    | Behaviour.TransferOutKey ->
        if spec.TransferOutKey.IsSome then
            Disposition.Honoured
        else
            Disposition.NotDeclared
    | Behaviour.Exportable ->
        if spec.Exportable then
            Disposition.Honoured
        else
            Disposition.NotDeclared

/// What the AG GRID ADAPTER does with one declared field — the answer this
/// phase exists to change from "nothing, silently" to this.
let agDisposition (sources: BindingResolver.BindingSources) (spec: GridSpec<'Msg>) (behaviour: Behaviour) =
    match behaviour with
    // Sort: AG's own sort model carries all three. `sortable` per column
    // follows the declaration (it was boxed `true` unconditionally before this
    // phase), the effective descriptor seeds `sort`, and `onSortChanged` writes
    // the descriptor back through the same `Action.SetState` the first-party
    // sortable header dispatches.
    | Behaviour.DefaultSort
    | Behaviour.SortStateKey
    | Behaviour.ColumnSortable -> firstPartyDisposition sources spec behaviour

    // Paging: AG's own pagination, EXCEPT over a host-paged source.
    | Behaviour.PageSize
    | Behaviour.PageStateKey ->
        match firstPartyDisposition sources spec behaviour with
        | Disposition.Honoured when hostPages spec -> Disposition.Inert agHostPagedPagination
        | other -> other

    // Editing and reorder: the SAME shipped destination rules, so the two
    // backends decline together and act together.
    | Behaviour.EditStateKey
    | Behaviour.ColumnEditable
    | Behaviour.Reorderable -> firstPartyDisposition sources spec behaviour

    // Transfer: refused by name.
    | Behaviour.TransferInKey ->
        if spec.TransferInKey.IsSome then
            Disposition.Inert agNoTransfer
        else
            Disposition.NotDeclared
    | Behaviour.TransferOutKey ->
        if spec.TransferOutKey.IsSome then
            Disposition.Inert agNoTransfer
        else
            Disposition.NotDeclared

    // Export: the control is renderer-owned chrome around whatever rendered the
    // grid (see `Render.fs`'s `gridAdapterChrome`), so there is ONE export
    // implementation and one egress path rather than a second copy inside the
    // adapter.
    | Behaviour.Exportable -> firstPartyDisposition sources spec behaviour

/// The disposition for a backend — one entry point so a caller cannot reach
/// only half the pair.
let disposition (backend: Backend) sources (spec: GridSpec<'Msg>) behaviour =
    match backend with
    | Backend.FirstParty -> firstPartyDisposition sources spec behaviour
    | Backend.AgGrid -> agDisposition sources spec behaviour

/// Every field this grid declares that the AG adapter does not act on, with the
/// reason. This is what the adapter reports at render time — the list being
/// EMPTY for a grid declaring nothing is what keeps an ordinary grid's console
/// as quiet as it was before this phase.
let agRefusals sources (spec: GridSpec<'Msg>) : (Behaviour * string) list =
    all
    |> List.choose (fun b ->
        match agDisposition sources spec b with
        | Disposition.Inert reason -> Some(b, reason)
        | _ -> None)

// ─── The concrete AG decisions ──────────────────────────────────────────────

/// Everything [AgGridAdapter.fs](AgGridAdapter.fs) needs to know, decided here
/// so the adapter holds no rule of its own.
type Plan =
    {
        /// The effective sort, from Phase 861's `effectiveSortDescriptor`: the
        /// state slot decides, a declared `defaultSort` fills the
        /// not-yet-sorted case.
        Sort: (int * SortDirection) option
        /// Per column, whether AG's header offers sorting. Phase 861's
        /// narrowing rule: sortable iff the grid names a sort state key AND the
        /// column projects a field AND the column has not opted out.
        ColumnSortable: bool list
        /// `(page-state key, page size, the page to open on)` when AG's own
        /// pagination is switched on. `None` leaves the pagination bar OFF,
        /// which is what makes it appear only where the spec declares it.
        Pagination: (string * int * int) option
        /// Per column, whether AG's cell is editable. The same three facts the
        /// first-party per-cell rule reads.
        ColumnEditable: bool list
        /// Where an edited cell's whole updated rows value commits, or `None`
        /// where no input may be drawn at all.
        EditCommit: Binding<Row seq> option
        /// Where a reordered row's whole updated rows value commits, or `None`
        /// where the drag handle must not be drawn.
        ReorderCommit: Binding<Row seq> option
        /// Declared fields this backend does not act on, with reasons.
        Refusals: (Behaviour * string) list
    }

/// Phase 861's per-column sort narrowing, in the spelling `Render.fs`'s
/// `sortableHeader` uses: `Some sortKey, Some field, (None | Some true)`.
let columnSortable (spec: GridSpec<'Msg>) : bool list =
    spec.Columns
    |> List.map (fun col ->
        match spec.SortStateKey, col.Field, col.Sortable with
        | Some _, Some _, (None | Some true) -> true
        | _ -> false)

/// Phase 863's per-column editability, read off the same three facts the
/// first-party cell reads: a column with its own `Value` closure projects
/// rather than addresses, a column with no `Field` has nothing to write to,
/// only `Text` and `Numeric` cells take a typed value, and `editable: false`
/// opts out under a grid-level `true`.
let columnEditable (spec: GridSpec<'Msg>) : bool list =
    spec.Columns
    |> List.map (fun col ->
        if not spec.Editable then
            false
        else
            match col.Value, col.Field, col.Kind, col.Editable with
            | _, _, _, Some false -> false
            | None, Some _, CellKindErased.Numeric, _ -> true
            | None, Some _, CellKindErased.Text, _ -> true
            | _ -> false)

/// The whole plan for one grid, against one render's binding sources.
///
/// `rowCount` is the resolved row count, needed only to clamp the opening page
/// — Phase 862's `clampPage`, so a position left past the end of a shrunken set
/// opens on the LAST page rather than on nothing.
let plan (sources: BindingResolver.BindingSources) (rowCount: int) (spec: GridSpec<'Msg>) : Plan =
    { Sort = BindingResolver.effectiveSortDescriptor spec.SortStateKey spec.DefaultSort sources
      ColumnSortable = columnSortable spec
      Pagination =
        match spec.PageStateKey, spec.PageSize with
        | Some key, Some size when size > 0 && not (hostPages spec) ->
            Some(key, size, BindingResolver.clampPage size (BindingResolver.readPageDescriptor sources key) rowCount)
        | _ -> None
      ColumnEditable = columnEditable spec
      EditCommit = BindingResolver.editDestination spec.Editable spec.EditStateKey spec.Source
      ReorderCommit = BindingResolver.reorderDestination spec.Reorderable spec.EditStateKey spec.Source
      Refusals = agRefusals sources spec }

/// The render-time refusal line, one per unacted declaration. Named, and it
/// names the NODE too — a page with four grids on it is where a bare field name
/// stops being actionable.
let refusalLine (nodeId: string) (behaviour: Behaviour, reason: string) : string =
    "AG Grid adapter: '"
    + name behaviour
    + "' is declared on grid '"
    + nodeId
    + "' but this backend does not act on it — "
    + reason

/// The sort descriptor the state key carries, in Phase 818's fixed shape —
/// `{"column": <index>, "direction": "asc"|"desc"}`, and the EMPTY object for
/// the third state of the cycle.
///
/// Phase 861's reasoning for the empty object rather than a cleared key is
/// load-bearing and is why this is a function rather than an inline literal at
/// each writer: a cleared key means "not yet sorted" and would re-apply
/// `defaultSort`, so a reader who asked for the emitter's order would be handed
/// the declared one instead.
let sortDescriptorJVal (descriptor: (int * SortDirection) option) : JVal =
    match descriptor with
    | None -> JObj []
    | Some(column, direction) ->
        JObj
            [ "column", JInt column
              "direction",
              JStr(
                  match direction with
                  | SortDirection.Asc -> "asc"
                  | SortDirection.Desc -> "desc"
              ) ]

/// The page descriptor the page-state key carries, in Phase 862's fixed shape.
let pageDescriptorJVal (page: int) : JVal = JObj [ "page", JInt(max 1 page) ]
