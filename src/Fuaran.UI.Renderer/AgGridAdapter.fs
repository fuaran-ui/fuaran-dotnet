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
//  OnRowClick wires AG Grid's `onRowClicked` event with the row obj.
//  ColumnWidth: Auto leaves AG Grid's auto-sizing; Fixed sets `width`;
//  Flex sets `flex`.
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

// ─── Per-column AG Grid columnDef builder ───────────────────────────

let private buildColumnDef<'Msg>
    (runAction: Action<'Msg> -> unit)
    (recurseRender: Node<'Msg> -> ReactElement)
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
        [ "headerName", box col.Label
          "valueGetter", box valueGetter
          "valueFormatter", box valueFormatter
          "sortable", box true
          "filter", box true
          "resizable", box true ]

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

                let coerced =
                    match currentVal with
                    | CellValue.Numeric _ ->
                        match newVal with
                        | :? float as f -> CellValue.Numeric f
                        | :? int as i -> CellValue.Numeric(float i)
                        | _ ->
                            let s = string newVal

                            match System.Double.TryParse(s) with
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

                Html.a
                    [ prop.className "fuaran-grid-cell-link"
                      prop.href (hrefFn row)
                      prop.text (textOf (labelFn row)) ]

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

    createObj (baseProps @ widthProps @ kindProps)

// ─── Top-level Grid render ──────────────────────────────────────────

let renderGrid<'Msg> (spec: GridSpec<'Msg>) (context: VisAdapter.VisualisationContext<'Msg>) : ReactElement option =
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
            let columnDefs =
                spec.Columns
                |> List.map (buildColumnDef context.RunAction context.RecurseRender)
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

            let onRowClicked (p: obj) =
                match spec.OnRowClick with
                | Some f -> context.RunAction(f (p?data: Row))
                | None -> ()

            let gridProps =
                createObj
                    [ "columnDefs" ==> columnDefs
                      "rowData" ==> List.toArray rows
                      "getRowId" ==> getRowId
                      "onRowClicked" ==> onRowClicked
                      "domLayout" ==> "autoHeight"
                      "animateRows" ==> true ]

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
