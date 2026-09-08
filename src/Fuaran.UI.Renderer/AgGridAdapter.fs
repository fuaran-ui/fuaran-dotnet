module Fuaran.UI.Renderer.AgGridAdapter

// ============================================================================
//  Fuaran — AG Grid adapter
//
//  Implements the `Grid` half of `IVisualisationAdapter<'Msg>` against the
//  `ag-grid-react` + `ag-grid-community` npm packages. AG Grid events
//  (`valueSetter`, `onRowClicked`) flow into typed `Action<'Msg>` via the
//  context's `RunAction` callback — no `obj` leakage at the author surface.
//
//  Standalone posture (Fuaran CLAUDE.md FGP 2 / "Standalone posture mandate"):
//  this file reaches the same ag-grid-react npm package a platform adapter's
//  AG Grid binding uses, via `Fable.Core.JsInterop` — NO external platform
//  project reference.
//  Consumers wanting the platform-wired tier consume that
//  platform adapter instead; this in-tree adapter lets the
//  Fuaran.UI.Renderer demo (and any standalone-posture consumer) reach the
//  same grid library without any platform-adapter dependency.
//
//  Cell-kind translation rules per [VisAdapter.fs](VisAdapter.fs) "AG Grid
//  event mapping":
//    Text / Numeric / Date → no cellRenderer; valueFormatter alone.
//    Editable f         → editable=true + valueSetter that coerces newValue
//                         to a CellValue case matching the column's current
//                         shape and dispatches `f (row, newCellValue)`.
//                         Returns false from valueSetter so AG Grid leaves
//                         the row obj alone; the consumer's Elmish update
//                         owns row state.
//    Checkbox(get, t)   → custom cellRenderer with <input type=checkbox>;
//                         change event dispatches `t (row, b)`.
//    Button / ButtonGroup → custom cellRenderer with <button>; click
//                         dispatches `onClick row`; stopPropagation so the
//                         per-row click handler doesn't also fire.
//    Link(href, label)  → custom cellRenderer with <a>; no Action wiring.
//    Pill / Progress    → custom cellRenderer mirroring the simple-HTML
//                         fallback shape; no Action wiring.
//    Custom render      → custom cellRenderer that calls
//                         `render (fun o -> jsToJVal o)` for the nested
//                         Fuaran Node, then recurses through `RecurseRender`.
//  AG Grid's `onRowClicked` wires to `Render.gridRowSelected` with the row obj
//  — the shared decision the first-party table also makes: dispatch
//  `OnRowClick` when the author supplied one, else publish the row under the
//  node's own id (the Phase 427 default, which is what a DECODED grid always
//  takes since a callback cannot cross the wire).
//  ColumnWidth: Auto leaves AG Grid's auto-sizing; Fixed sets `width`;
//  Flex sets `flex`.
//
//  ── The declarative grid vocabulary (Phase 1611) ──────────────────────────
//  Six behaviours reached the wire between Phases 861 and 1125 and this
//  adapter read none of them, so the same decoded tree behaved differently on
//  the two grid backends. Every one of them is now either HONOURED here or
//  REFUSED BY NAME at render time through `Diagnostics.warn`; nothing is
//  dropped silently. The judgement itself is made in
//  [AgGridPlan.fs](AgGridPlan.fs), over the same shipped `BindingResolver`
//  predicates the first-party leg calls, because this file is Fable-only and
//  no test in this repo can reach it.
//
//    HONOURED
//      defaultSort         seeds AG's sort model through the effective
//      sortStateKey        descriptor (state decides, defaultSort fills the
//                          not-yet-sorted case); `onSortChanged` writes the
//                          descriptor back with the same `Action.SetState`,
//                          key and shape the first-party sortable header uses.
//                          The ORDER is the runtime's on both backends: a
//                          comparator pinned to "equal" keeps AG from
//                          re-sorting rows Phase 861's sorter already ordered.
//      columns[].sortable  narrows per column — `sortable` was boxed `true`
//                          unconditionally before this phase, so the affordance
//                          appeared on columns the wire had opted out and on
//                          grids naming no sort state key at all.
//      pageSize            AG's own pagination; the bar is on ONLY where both
//      pageStateKey        are declared, the opening page is applied on ready,
//                          and the reader's page round-trips through the key.
//      editStateKey        per-column `editable` + a `valueSetter` committing
//      columns[].editable  the whole updated rows value to Phase 863's declared
//                          destination, through the renderer's own write path
//                          — so the commit crosses the same scope routing and
//                          host-reserved-key refusal a first-party edit does.
//      reorderable         AG row drag on the first column; the move is
//                          `BindingResolver.moveRow` and the commit is the same
//                          whole-rows write, to the same destination.
//      exportable          honoured by RENDERER-OWNED chrome around whatever
//                          drew the grid (`Render.fs`), which is where it sits
//                          on the first-party leg too — so the serialiser, the
//                          scope rule, the filename rule and the dispatch gate
//                          have exactly one implementation.
//
//    REFUSED BY NAME (the reason is carried in `AgGridPlan`, printed with the
//    field name and the node id)
//      pageStateKey        over a HOST-PAGED source only: AG's client-side row
//                          model derives its pager from the rows it holds, so
//                          it would report a one-page total over a host's page.
//      transferInKey       cross-grid transfer needs a drop-zone registry
//      transferOutKey      across grid instances, and this adapter is invoked
//                          once per grid with no handle to its siblings.
//
//  `keepRowsTogether` / `repeatHeader` are PAGED-MEDIA declarations the
//  first-party leg carries as CSS on a semantic `<table>`; AG renders its own
//  DOM, so they are not this adapter's to place and are deliberately outside
//  the set above rather than refused within it.
// ============================================================================

#nowarn "1182" // unused values for Fable imports

open Fable.Core
open Fable.Core.JsInterop
open Fable.React
open Feliz
open Fuaran.UI.Types

// ─── npm imports ─────────────────────────────────────────────────────

let private allCommunityModule: obj =
    import "AllCommunityModule" "ag-grid-community"

let private moduleRegistry: obj = import "ModuleRegistry" "ag-grid-community"

let private agGridReact: obj = import "AgGridReact" "ag-grid-react"

let mutable private modulesRegistered = false

let private ensureModulesRegistered () =
    if not modulesRegistered then
        moduleRegistry?registerModules ([| allCommunityModule |])
        modulesRegistered <- true

// ─── Formatting helpers ─────────────────────────────────────────────
//
// Duplicates `Render.fs`'s private `formatNumber` / `renderCellValue` so the
// adapter doesn't reach into the renderer's private surface. The shapes
// must stay in lockstep — keep these aligned with the originals during
// any future format change.

let private formatNumber (format: CellFormat) (value: float) : string =
    match format with
    | CellFormat.None -> string value
    | CellFormat.Number(Some decimals) -> sprintf "%.*f" decimals value
    | CellFormat.Number None ->
        if value = floor value then
            sprintf "%.0f" value
        else
            sprintf "%g" value
    | CellFormat.Currency code -> sprintf "%s %.2f" code value
    | CellFormat.Percent(Some decimals) -> sprintf "%.*f%%" decimals (value * 100.0)
    | CellFormat.Percent None -> sprintf "%.1f%%" (value * 100.0)
    | CellFormat.SignificantDigits digits -> sprintf "%.*g" digits value
    | CellFormat.Date _ -> string value
    // Phase 819 — shared Renderer.Core helpers, keeping the adapter in
    // lockstep with `Render.fs`'s formatNumber (see the note above).
    | CellFormat.Duration(unit, style) -> Formatting.formatDuration unit style value
    | CellFormat.RelativeTime unit -> Formatting.formatRelativeEnglish unit value
    | CellFormat.Custom f -> f (CellValue.Numeric value)

let private renderCellValue (format: CellFormat) (value: CellValue) : string =
    match format with
    | CellFormat.Custom f -> f value
    | _ ->
        match value with
        | CellValue.Numeric n -> formatNumber format n
        | CellValue.Text s -> s
        | CellValue.Bool b -> if b then "true" else "false"
        | CellValue.Date d ->
            match format with
            | CellFormat.Date fmt -> d.ToString(fmt)
            | _ -> d.ToString("yyyy-MM-dd")
        | CellValue.Empty -> ""

let private textOf (text: TextSource) : string =
    match text with
    | TextSource.Literal s -> s
    | TextSource.Bound _ -> ""
    | TextSource.I18n(key, _) -> sprintf "[i18n:%s]" key

// Deterministic correlation id (Phase 138) — seeded from the binding error
// message so the same failing grid renders byte-identical output (cache-
// stable + SSR/hydration-parity-safe), matching the core renderer's posture.
let private correlationId (seed: string) : string = Ids.deterministicCorrelationId seed

/// Coerce AG Grid's `newValue` into a `CellValue` matching the shape the cell
/// currently holds.
///
/// Hoisted by Phase 1611 because there are now TWO edit paths through this
/// adapter — the authored `CellKindErased.Editable` closure, and the
/// DECLARATIVE `editable` + `editStateKey` path a decoded grid takes — and two
/// copies of a coercion rule is how one grid comes to parse its numbers two
/// ways depending on which spelling its author reached for.
let private coerceToCellShape (currentVal: CellValue) (newVal: obj) : CellValue =
    match currentVal with
    | CellValue.Numeric _ ->
        match newVal with
        | :? float as f -> CellValue.Numeric f
        | :? int as i -> CellValue.Numeric(float i)
        | _ ->
            let s = string newVal

            // Invariant on the .NET leg — see `GridPaste`: an edited cell is
            // canonically encoded, so the pipelines must agree, and the
            // single-argument BCL overload reads CurrentCulture.
            let parsed =
#if FABLE_COMPILER
                System.Double.TryParse s
#else
                System.Double.TryParse(
                    s,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture
                )
#endif

            match parsed with
            | true, f -> CellValue.Numeric f
            | _ -> CellValue.Text s
    | CellValue.Bool _ ->
        match newVal with
        | :? bool as b -> CellValue.Bool b
        | _ -> CellValue.Bool(string newVal = "true")
    | CellValue.Date _ ->
        match newVal with
        | :? System.DateTimeOffset as d -> CellValue.Date d
        | _ -> CellValue.Text(string newVal)
    | _ -> CellValue.Text(string newVal)

// ─── Per-column AG Grid columnDef builder ───────────────────────────

let private buildColumnDef<'Msg>
    (runAction: Action<'Msg> -> unit)
    (recurseRender: Node<'Msg> -> ReactElement)
    // Phase 1523 — the composition's ambient destination policy, threaded from
    // `VisualisationContext` so this adapter's `Link` cell gates identically to
    // the first-party simple table's.
    (egressPolicy: Sanitize.EgressPolicy)
    // Phase 1611 — this column's DECLARED dispositions, decided in
    // [AgGridPlan.fs](AgGridPlan.fs) rather than here. `sortable` was boxed
    // `true` on every column before this phase, so a column that declared
    // `sortable: false`, and every column of a grid that named no sort state
    // key at all, offered a sort affordance the wire refused. `editable` is
    // Phase 863's narrowing, and `commit` the destination the whole updated
    // rows value goes to.
    (colId: string)
    (sortable: bool)
    (initialSort: string option)
    (rowDrag: bool)
    (editable: bool)
    (commit: (int -> string -> obj -> unit) option)
    (col: ColumnErased<'Msg>)
    : obj =
    // Phase 425 — the closure wins; else the declarative `Field` projects the row property; else empty.
    let cellValue (row: Row) : CellValue =
        match col.Value with
        | Some accessor -> accessor row
        | None ->
            match col.Field with
            | Some field -> BindingResolver.projectRowFieldValue row field
            | None -> CellValue.Empty

    let cellValueOf (p: obj) : CellValue =
        let row: Row = p?data

        if isNull (box row) then CellValue.Empty else cellValue row

    let valueGetter (p: obj) : obj =
        let row: Row = p?data

        if isNull (box row) then
            null
        else
            match cellValue row with
            | CellValue.Numeric n -> box n
            | CellValue.Text s -> box s
            | CellValue.Bool b -> box b
            | CellValue.Date d -> box d
            | CellValue.Empty -> null

    let valueFormatter (p: obj) : string =
        renderCellValue col.Format (cellValueOf p)

    let baseProps: (string * obj) list =
        [ // A stable, index-derived column id. AG derives one from `field`
          // otherwise, and this adapter sets no `field` (it projects through
          // `valueGetter`), so the ids would be positional strings AG chose —
          // which the sort round trip has to parse back into the column INDEX
          // the wire's descriptor carries. Setting it makes that mapping ours.
          "colId", box colId
          "headerName", box col.Label
          "valueGetter", box valueGetter
          "valueFormatter", box valueFormatter
          // Phase 1611 — the DECLARATION decides, not the adapter. `sortable`
          // was boxed `true` on every column before this phase, so a column
          // declaring `sortable: false`, and every column of a grid naming no
          // `sortStateKey` at all, offered a sort affordance the wire refused.
          "sortable", box sortable
          "filter", box true
          "resizable", box true ]
        @ (match initialSort with
           | Some direction when sortable -> [ "sort", box direction ]
           | _ -> [])
        // Phase 1611 — the ORDER is the runtime's, on both backends. AG's
        // header raises the gesture and shows the indicator; the descriptor it
        // produces is written to `sortStateKey`, the re-render re-resolves it
        // through Phase 861's `sortRowsByDescriptor`, and the rows arrive here
        // already in that order. A comparator that says "these are equal" keeps
        // AG's stable sort from re-ordering them on top — so there is ONE
        // sorting implementation across the two backends rather than two that
        // agree about the column and disagree about everything else (empties,
        // ties, mixed cell types).
        @ (if sortable then
               [ "comparator", box (fun (_: obj) (_: obj) -> 0) ]
           else
               [])
        @ (if rowDrag then [ "rowDrag", box true ] else [])

    let widthProps: (string * obj) list =
        match col.Width with
        | ColumnWidth.Auto -> []
        | ColumnWidth.Fixed pixels -> [ "width", box pixels ]
        | ColumnWidth.Flex weight -> [ "flex", box weight ]

    // Per-kind extensions: editable, checkbox, button, link, pill, progress, custom.
    let kindProps: (string * obj) list =
        match col.Kind with
        | CellKindErased.Text
        | CellKindErased.Numeric
        | CellKindErased.Date -> []

        | CellKindErased.Editable onEdit ->
            // AG Grid's valueSetter callback receives newValue + data; we
            // coerce newValue to a CellValue matching the column's current
            // shape and dispatch the typed Action via runAction. Returning
            // `false` tells AG Grid not to mutate the row obj — Elmish's
            // dispatched message is the canonical update channel.
            let valueSetter (p: obj) : bool =
                let row: Row = p?data
                let newVal: obj = p?newValue
                let currentVal = cellValueOf p

                let coerced = coerceToCellShape currentVal newVal

                match onEdit with
                | Some onEdit -> runAction (onEdit (row, coerced))
                | None -> () // no handler — inert edit, same as the old no-op action

                false

            [ "editable", box true; "valueSetter", box valueSetter ]

        | CellKindErased.Checkbox(getValue, onToggle) ->
            let cellRenderer (p: obj) : ReactElement =
                let row: Row = p?data
                let current = getValue row

                Html.input
                    [ prop.type'.checkbox
                      prop.isChecked current
                      prop.onChange (fun (b: bool) ->
                          match onToggle with
                          | Some onToggle -> runAction (onToggle (row, b))
                          | None -> ()) ]

            [ "cellRenderer", box cellRenderer ]

        | CellKindErased.Button(label, onClick) ->
            let labelText = textOf label

            let cellRenderer (p: obj) : ReactElement =
                let row: Row = p?data

                Html.button
                    [ prop.className "fuaran-grid-cell-button"
                      prop.text labelText
                      prop.onClick (fun e ->
                          e.stopPropagation ()

                          match onClick with
                          | Some onClick -> runAction (onClick row)
                          | None -> ()) ]

            [ "cellRenderer", box cellRenderer ]

        | CellKindErased.ButtonGroup buttons ->
            let cellRenderer (p: obj) : ReactElement =
                let row: Row = p?data

                Html.span
                    [ prop.className "fuaran-grid-cell-button-group"
                      prop.children
                          [ for item in buttons ->
                                Html.button
                                    [ prop.className "fuaran-grid-cell-button"
                                      prop.text (textOf item.Label)
                                      prop.onClick (fun e ->
                                          e.stopPropagation ()

                                          match item.OnClick with
                                          | Some onClick -> runAction (onClick row)
                                          | None -> ()) ] ] ]

            [ "cellRenderer", box cellRenderer ]

        | CellKindErased.Link(hrefFn, labelFn) ->
            let cellRenderer (p: obj) : ReactElement =
                let row: Row = p?data

                // Phase 1523 — the same two gates the simple-table `Link` cell
                // runs, in the same order: the scheme floor says what the URL
                // may BE, the destination policy says where it may GO. Before
                // this the href was emitted raw here, so an adapter-backed grid
                // rendered `javascript:` and off-origin destinations that the
                // first-party table refused in the same document.
                let safeHref, egressAttrs =
                    Sanitize.sanitizeUrlForEgress egressPolicy Sanitize.EgressClass.Hyperlink (hrefFn row)

                Html.a (
                    [ prop.className "fuaran-grid-cell-link"
                      prop.href safeHref
                      prop.text (textOf (labelFn row)) ]
                    @ (egressAttrs |> List.map (fun (k, v) -> prop.custom (k, v)))
                )

            [ "cellRenderer", box cellRenderer ]

        | CellKindErased.Pill(labelFn, toneFn) ->
            let cellRenderer (p: obj) : ReactElement =
                let row: Row = p?data

                Html.span
                    [ prop.className (sprintf "fuaran-grid-cell-pill fuaran-pill-%s" (Theme.toneVar (toneFn row)))
                      prop.text (textOf (labelFn row)) ]

            [ "cellRenderer", box cellRenderer ]

        | CellKindErased.TonedPill(field, toneMap, defaultTone) ->
            // Phase 750 — the declarative twin, through the same shared lowering the
            // simple-table cell uses, so the two grid backends cannot disagree about
            // an unmapped value.
            let cellRenderer (p: obj) : ReactElement =
                let row: Row = p?data
                let label, tone = BindingResolver.tonedPillOf row field toneMap defaultTone

                Html.span
                    [ prop.className (sprintf "fuaran-grid-cell-pill fuaran-pill-%s" (Theme.toneVar tone))
                      prop.text label ]

            [ "cellRenderer", box cellRenderer ]

        | CellKindErased.Progress(fractionFn, labelFn) ->
            let cellRenderer (p: obj) : ReactElement =
                let row: Row = p?data
                let f = fractionFn row

                Html.div
                    [ prop.className "fuaran-grid-cell-progress"
                      prop.children
                          [ Html.div
                                [ prop.className "fuaran-grid-cell-progress-fill"
                                  prop.style [ style.width (length.percent (f * 100.0)) ] ]
                            match labelFn with
                            | Some l -> Html.span [ prop.text (textOf (l row)) ]
                            | None -> Html.none ] ]

            [ "cellRenderer", box cellRenderer ]

        | CellKindErased.Custom render ->
            let cellRenderer (p: obj) : ReactElement =
                let row: Row = p?data
                // The custom cell renderer captures a `row -> JVal` accessor
                // shape; bridge the JS row object to the structured JVal and
                // let the author's render decide what to do with it.
                let nestedNode = render (fun _ -> Runtime.JsonBridge.jsToJVal row)
                recurseRender nestedNode

            [ "cellRenderer", box cellRenderer ]

    // Phase 1611 — the DECLARATIVE edit path, and the one this phase exists
    // for. `CellKindErased.Editable` above is the AUTHORED shape: a closure,
    // which cannot cross the wire, so it is `None` on every decoded grid. A
    // decoded grid says it is editable by declaring `editable` on the grid and
    // naming its destination with `editStateKey` (Phase 863) — and neither name
    // appeared in this file, so the adapter rendered exactly the read-only grid
    // the first-party leg rendered as a set of inputs.
    //
    // Which columns take a value, and where the value goes, are both decided in
    // [AgGridPlan.fs](AgGridPlan.fs) off the shipped rules, so this arm holds no
    // rule of its own. `false` is returned for the same reason the authored arm
    // returns it: the commit goes to the declared destination and the re-render
    // brings the new rows back, so AG must not also mutate its own row object.
    let declaredEditProps: (string * obj) list =
        match editable, commit, col.Field with
        | true, Some commit, Some field ->
            let valueSetter (p: obj) : bool =
                let rowIndex: int = p?node?rowIndex
                let coerced = coerceToCellShape (cellValueOf p) (p?newValue: obj)

                match coerced with
                | CellValue.Numeric f -> commit rowIndex field (box f)
                | CellValue.Text s -> commit rowIndex field (box s)
                | CellValue.Bool b -> commit rowIndex field (box b)
                | CellValue.Date d -> commit rowIndex field (box d)
                | CellValue.Empty -> ()

                false

            [ "editable", box true; "valueSetter", box valueSetter ]
        | _ -> []

    // The authored arm's own `editable` / `valueSetter` win where both apply:
    // `createObj` takes the LAST value for a repeated key, and a column that is
    // both `CellKindErased.Editable` and declaratively editable was authored
    // in-process with a handler that must not be bypassed.
    createObj (baseProps @ widthProps @ declaredEditProps @ kindProps)

// ─── Top-level Grid render ──────────────────────────────────────────

/// `renderGrid`, plus the Phase 1594 raw-handle escape valve: `onReady`, when
/// the host supplied one, is invoked with AG Grid's own grid API once the grid
/// signals readiness.
///
/// The hook is a PARAMETER and never a member of `GridSpec` — it is supplied by
/// the host when it constructs the adapter ([AgAdapter.fs](AgAdapter.fs)), so no
/// decoded tree can carry it, reach it or trigger it. That is a structural
/// guarantee rather than the weaker "a closure cannot cross the wire" argument
/// that `GridSpec.OnRowClick` rests on: it is not a member of any wire type,
/// so there is nothing for a decoder to fill.
///
/// Once-per-instance comes from AG Grid itself: `onGridReady` is raised when the
/// grid API becomes available and not again for the life of that grid, so a
/// re-render with fresh props does not re-enter the host's hook.
let renderGridWithReady<'Msg>
    (onReady: (obj -> unit) option)
    (spec: GridSpec<'Msg>)
    (context: VisAdapter.VisualisationContext<'Msg>)
    : ReactElement option =
    ensureModulesRegistered ()

    // State-slot dispatch — adapter mirrors the simple-HTML fallback's
    // shape for OnLoading / OnError; OnEmpty fires once rows resolve.
    // Returning Some commits the slot's Node to the rendered output, so
    // the renderer doesn't also run its fallback.
    let resolution = BindingResolver.resolve<Row seq> context.Sources spec.Source

    let stateNode =
        match resolution, context.State.OnLoading, context.State.OnError with
        | BindingResolver.NotResolved, Some loadingNode, _ -> Some loadingNode
        | BindingResolver.Errored msg, _, Some errorFn ->
            Some(
                errorFn
                    { Kind = ErrorKind.BindingResolution
                      Message = msg
                      CorrelationId = correlationId msg }
            )
        | _ -> None

    match stateNode with
    | Some node -> Some(context.RecurseRender node)
    | None ->
        let rows =
            match resolution with
            | BindingResolver.Resolved seq -> Seq.toList seq
            | _ -> []

        match rows, context.State.OnEmpty with
        | [], Some emptyNode -> Some(context.RecurseRender emptyNode)
        | _ ->
            // ── Phase 1611 — the declarative grid vocabulary ───────────────
            //
            // Every judgement below is made in [AgGridPlan.fs](AgGridPlan.fs),
            // over the SAME shipped predicates the first-party leg calls, and
            // is proven there against the corpus's grid fixtures. This file
            // applies the result to AG's props and holds no rule of its own,
            // because it is Fable-only and therefore unreachable by any test in
            // this repo.
            let plan = AgGridPlan.plan context.Sources (List.length rows) spec

            // Named refusals, at render time. A grid whose declarations this
            // backend acts on in full reports nothing, so an ordinary grid's
            // console is exactly as quiet as it was before this phase; a grid
            // that declares something this backend cannot reach says so, with
            // the field name, the node id and the reason. That line is the
            // whole difference between a refusal and the silent drop this
            // phase found.
            for refusal in plan.Refusals do
                Diagnostics.warn (AgGridPlan.refusalLine context.NodeId refusal) null

            // The rows in the order the declaration puts them in — Phase 861's
            // sorter, the same call `Render.fs` makes. AG's own comparator is
            // pinned to "equal" on every sortable column (see `buildColumnDef`)
            // so the header raises the gesture while this stays the single
            // ordering implementation across both backends.
            let sortedRows = rows |> BindingResolver.sortRowsByDescriptor spec.Columns plan.Sort

            // Phase 863's whole-rows commit, through the renderer's own write
            // path (`VisualisationContext.WriteRows`) rather than a store this
            // adapter reached itself — so an adapter-backed edit crosses Phase
            // 266's scope routing and Phase 782's host-reserved-key refusal
            // exactly as a first-party one does.
            let editCommit: (int -> string -> obj -> unit) option =
                plan.EditCommit
                |> Option.map (fun destination ->
                    fun (rowIndex: int) (field: string) (newValue: obj) ->
                        let newRows =
                            sortedRows
                            |> List.mapi (fun i row -> if i = rowIndex then Map.add field newValue row else row)

                        context.WriteRows destination (Seq.ofList newRows))

            let columnDefs =
                spec.Columns
                |> List.mapi (fun index col ->
                    let initialSort =
                        match plan.Sort with
                        | Some(c, SortDirection.Asc) when c = index -> Some "asc"
                        | Some(c, SortDirection.Desc) when c = index -> Some "desc"
                        | _ -> None

                    buildColumnDef
                        context.RunAction
                        context.RecurseRender
                        context.EgressPolicy
                        (string index)
                        (List.item index plan.ColumnSortable)
                        initialSort
                        // Phase 934 — the drag handle rides the FIRST column, the
                        // one position every grid has. It is drawn only where a
                        // reorder has somewhere to commit, which is the same
                        // `reorderDestination` test the first-party handle makes.
                        (plan.ReorderCommit.IsSome && index = 0)
                        (List.item index plan.ColumnEditable)
                        editCommit
                        col)
                |> List.toArray

            // Phase 425 — the row-key closure wins; else the declarative `RowKeyField` projects the
            // row property; else an empty id (AG Grid falls back to its own row index).
            let getRowId (p: obj) : string =
                let row: Row = p?data

                match spec.RowKey with
                | Some rk -> rk row
                | None ->
                    match spec.RowKeyField with
                    | Some field -> BindingResolver.projectRowFieldString row field
                    | None -> ""

            // The same decision the first-party table's `<tr>` handler makes —
            // dispatch `OnRowClick`, or publish the row under this node's own id
            // (Phase 427). The `None` arm used to be `()`, so a DECODED grid —
            // where the slot is always `None`, because a callback cannot cross
            // the wire — published nothing, and every `Binding.Selection` reader
            // of that grid stayed empty on this path while working on the
            // first-party one. Master-detail with no host code is the whole
            // point of the default; an adapter must not be where it stops.
            let onRowClicked (p: obj) =
                Render.gridRowSelected context.RunAction context.NodeId spec (p?data: Row)

            // Phase 861/818 — the sort ROUND TRIP. AG's header raises the
            // gesture; the descriptor it produces is written to the grid's own
            // `sortStateKey` through the same `Action.SetState` the first-party
            // sortable header dispatches, to the same key, in Phase 818's fixed
            // shape. So the two backends drive one state slot, and a `Binding`
            // reading that slot cannot tell which grid wrote it.
            //
            // The write is guarded on the descriptor having actually MOVED. AG
            // raises `onSortChanged` while applying the state we handed it, so
            // an unguarded write would set the slot to what it already holds and
            // re-render on every mount.
            let sortProps =
                match spec.SortStateKey with
                | None -> []
                | Some sortKey ->
                    let onSortChanged (p: obj) =
                        let api: obj = p?api

                        let next: (int * SortDirection) option =
                            if isNull api then
                                plan.Sort
                            else
                                let states: obj[] = api?getColumnState ()

                                states
                                |> Array.tryFind (fun st -> not (isNull (st?sort: obj)))
                                |> Option.bind (fun st ->
                                    let colId: string = st?colId
                                    let direction: string = st?sort

                                    match System.Int32.TryParse colId with
                                    | true, index ->
                                        Some(
                                            index,
                                            (if direction = "desc" then
                                                 SortDirection.Desc
                                             else
                                                 SortDirection.Asc)
                                        )
                                    | _ -> None)

                        if next <> plan.Sort then
                            context.RunAction(Action.SetState(sortKey, Some(AgGridPlan.sortDescriptorJVal next), None))

                    [ "onSortChanged" ==> onSortChanged ]

            // Phase 862 — the pagination bar is on ONLY where the spec declares
            // it, which is what `plan.Pagination` being `None` says. AG owns the
            // slicing on this backend (the runtime does not slice as well —
            // `rowData` is the whole sorted set), and the page the reader is on
            // round-trips through `pageStateKey` in Phase 862's fixed shape,
            // guarded against the mount-time echo exactly as the sort write is.
            let paginationProps =
                match plan.Pagination with
                | None -> []
                | Some(pageKey, pageSize, page) ->
                    let onPaginationChanged (p: obj) =
                        let api: obj = p?api

                        if not (isNull api) then
                            let current: int = api?paginationGetCurrentPage ()

                            if current + 1 <> page then
                                context.RunAction(
                                    Action.SetState(pageKey, Some(AgGridPlan.pageDescriptorJVal (current + 1)), None)
                                )

                    [ "pagination" ==> true
                      "paginationPageSize" ==> pageSize
                      "onPaginationChanged" ==> onPaginationChanged ]

            // Phase 934 — the reorder commit. The handle is on the first column
            // (see `columnDefs`); the move itself is `BindingResolver.moveRow`,
            // the same function the first-party handle calls, over the same
            // SORTED list, writing the same whole-rows value to the same
            // destination. `rowDragManaged` stays OFF deliberately: a managed
            // drag would have AG reorder its own copy while the destination
            // write reorders the tree's, and the two orders would then differ
            // for exactly as long as the re-render took.
            let reorderProps =
                match plan.ReorderCommit with
                | None -> []
                | Some destination ->
                    let onRowDragEnd (p: obj) =
                        let fromIndex: int = p?node?rowIndex
                        let toIndex: int = p?overIndex

                        context.WriteRows
                            destination
                            (BindingResolver.moveRow fromIndex toIndex sortedRows |> Seq.ofList)

                    [ "rowDragManaged" ==> false; "onRowDragEnd" ==> onRowDragEnd ]

            // Phase 1594 — the escape valve, now sharing `onGridReady` with the
            // one thing AG's pagination cannot be told declaratively: which page
            // to open on. The host's hook is invoked with AG Grid's own grid API
            // (`GridReadyEvent.api`) — unwrapped, unvalidated and unmediated;
            // see the escape-hatch inventory's Hatch 14 — after the opening page
            // is applied, so a host that moves the grid on ready has the last
            // word rather than being silently overridden.
            let readyProps =
                let openingPage =
                    match plan.Pagination with
                    | Some(_, _, page) when page > 1 -> Some page
                    | _ -> None

                match onReady, openingPage with
                | None, None -> []
                | _ ->
                    [ "onGridReady"
                      ==> (fun (p: obj) ->
                          let api: obj = p?api

                          match openingPage with
                          | Some page when not (isNull api) -> api?paginationGoToPage (page - 1)
                          | _ -> ()

                          match onReady with
                          | Some hook -> hook api
                          | None -> ()) ]

            let gridProps =
                createObj (
                    [ "columnDefs" ==> columnDefs
                      "rowData" ==> List.toArray sortedRows
                      "getRowId" ==> getRowId
                      "onRowClicked" ==> onRowClicked
                      "domLayout" ==> "autoHeight"
                      "animateRows" ==> true ]
                    @ sortProps
                    @ paginationProps
                    @ reorderProps
                    @ readyProps
                )

            let gridElement =
                ReactLegacy.createElement (unbox<ReactElement> agGridReact, gridProps)

            // Wrap in a div carrying the ag-theme-alpine class so AG Grid's
            // stylesheet (loaded via the consumer's CSS imports) applies.
            // The fuaran-grid class keeps the renderer's own visual hooks
            // applicable (border / spacing rules in the host stylesheet).
            Some(
                Html.div
                    [ prop.className "fuaran-grid ag-theme-alpine"
                      prop.style [ style.width (length.percent 100); style.minHeight 200 ]
                      prop.children [ gridElement ] ]
            )

/// `renderGridWithReady` with no ready hook — the shape every caller had before
/// Phase 1594, kept so the addition is additive rather than a signature change.
let renderGrid<'Msg> (spec: GridSpec<'Msg>) (context: VisAdapter.VisualisationContext<'Msg>) : ReactElement option =
    renderGridWithReady None spec context
