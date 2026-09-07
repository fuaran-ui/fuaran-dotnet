module Fuaran.UI.Renderer.AgChartAdapter

// ============================================================================
//  Fuaran — AG Charts adapter
//
//  Implements the `Chart` half of `IVisualisationAdapter<'Msg>` against the
//  `ag-charts-react` + `ag-charts-community` npm packages. AG Charts
//  `listeners.nodeClick` event datum flows into `Render.chartPointSelected` —
//  the shared decision the first-party lowered-SVG path also makes — so an
//  authored `ChartSpec.OnPointClick` dispatches through the context's
//  `RunAction` callback and a DECODED chart (whose slot is always `None`,
//  because a callback cannot cross the wire) falls to the Phase 933 default and
//  publishes the datum under its own node id. No `obj` leakage at the author
//  surface either way.
//
//  Standalone posture (Fuaran CLAUDE.md FGP 2): reaches ag-charts-react via
//  `Fable.Core.JsInterop`, no external platform project reference.
//
//  ChartKind → AG Charts type mapping per [VisAdapter.fs](VisAdapter.fs)
//  "AG Charts event mapping":
//    Line / Bar / Area / Scatter → AG Charts `type: "line"` / `"bar"` /
//      `"area"` / `"scatter"`.
//    Pie → AG Charts `type: "pie"` (single series, angleKey = first YField,
//      legendItemKey = XField).
//  YFields → one series per field with xKey = XField, yKey = field.
// ============================================================================

#nowarn "1182"

open Fable.Core
open Fable.Core.JsInterop
open Fable.React
open Feliz
open Fuaran.UI.Types

// ─── npm imports ─────────────────────────────────────────────────────

let private agChartsCommunityModule: obj =
    import "AgChartsCommunityModule" "ag-charts-community"

let private agCharts: obj = import "AgCharts" "ag-charts-react"

let mutable private chartsModulesRegistered = false

let private ensureChartsModulesRegistered () =
    if not chartsModulesRegistered then
        // AG Charts v13+ module registration — `setup()` registers all
        // Community modules in Integrated mode (chart + axes + series).
        // Mirrors the platform adapter's chart path.
        agChartsCommunityModule?setup ()
        chartsModulesRegistered <- true

// Deterministic correlation id (Phase 138) — seeded from the binding error
// message so the same failing chart renders byte-identical output (cache-
// stable + SSR/hydration-parity-safe), matching the core renderer's posture.
let private correlationId (seed: string) : string = Ids.deterministicCorrelationId seed

let private textOf (text: TextSource) : string =
    match text with
    | TextSource.Literal s -> s
    | TextSource.Bound _ -> ""
    | TextSource.I18n(key, _) -> sprintf "[i18n:%s]" key

// ─── ChartKind translation ──────────────────────────────────────────

let private chartKindString (kind: ChartKind) : string =
    match kind with
    | ChartKind.Line -> "line"
    | ChartKind.Bar -> "bar"
    | ChartKind.Area -> "area"
    | ChartKind.Pie -> "pie"
    | ChartKind.Scatter -> "scatter"
    // AG Charts ships
    // `heatmap` as an Enterprise-tier series. Consumers must declare
    // `ag-charts-enterprise` in their package.json (`optionalDependencies`
    // is sufficient when bundled at boot). Without enterprise loaded,
    // AG Charts emits a console warning and the chart renders empty —
    // visible failure mode, not silent.
    | ChartKind.Heatmap -> "heatmap"

// ─── Series builder ─────────────────────────────────────────────────

let private buildSeries<'Msg> (runAction: Action<'Msg> -> unit) (nodeId: string) (spec: ChartSpec<'Msg>) : obj array =
    // listeners.nodeClick: AG Charts passes `{ datum: obj; series: ...; ... }`.
    // We extract `datum` (the row) and hand it to the SAME decision the
    // first-party lowered-SVG path makes — dispatch the author's
    // `OnPointClick`, or publish the datum under this node's own id.
    //
    // ATTACHED UNCONDITIONALLY, which is the whole of this change. The listener
    // used to exist only in the `Some` case, so a DECODED chart — where the slot
    // is always `None`, because a callback cannot cross the wire — got no
    // listener at all and its point-selection affordance was inert on this path
    // while working on the first-party one. An adapter is a rendering choice; it
    // is not supposed to be a behaviour choice.
    //
    // No mark-id round-trip here: AG Charts hands the listener the row itself,
    // where the lowered path has to recover it from `data-fuaran-mark` on the
    // emitted SVG. That difference is exactly why the decision is
    // `Render.chartPointSelected` (takes a row) rather than
    // `Render.chartPointClick` (takes a mark id).
    let listeners =
        let nodeClick (ev: obj) =
            let datum: Row = ev?datum
            Render.chartPointSelected runAction nodeId spec datum

        createObj [ "nodeClick" ==> nodeClick ]

    match spec.Kind with
    | ChartKind.Pie ->
        // Pie series uses angleKey / legendItemKey instead of xKey / yKey.
        // Use the first YField as the angle measure; XField as legend key.
        let angleKey =
            match spec.YFields with
            | first :: _ -> first
            | [] -> ""

        [| createObj
               [ "type" ==> "pie"
                 "angleKey" ==> angleKey
                 "legendItemKey" ==> spec.XField
                 "listeners" ==> listeners ] |]
    | ChartKind.Heatmap ->
        // AG Charts heatmap series shape: xKey (column axis), yKey (row axis),
        // colorKey (the numeric value). Per the ChartKind.Heatmap doc:
        // YFields[0] = row-axis category key; YFields[1] = colour-value key.
        // If YFields has < 2 entries the series renders empty (visible
        // failure mode rather than a silent type-mismatch). Requires
        // AG Charts Enterprise loaded; community-tier emits a console
        // warning and renders nothing.
        let yKey, colorKey =
            match spec.YFields with
            | y :: c :: _ -> y, c
            | y :: _ -> y, ""
            | [] -> "", ""

        [| createObj
               [ "type" ==> "heatmap"
                 "xKey" ==> spec.XField
                 "yKey" ==> yKey
                 "colorKey" ==> colorKey
                 "listeners" ==> listeners ] |]
    | _ ->
        let seriesType = chartKindString spec.Kind

        // ChartSpec.Stacked
        // routes to AG Charts' per-series `stacked: true`. Multiple YFields
        // sharing the same xKey + stacked = true automatically stack as a
        // single group; no `stackGroup` key needed when only one stack.
        // The field is meaningful for Bar / Area; ignored for Line /
        // Scatter (chartKindString doesn't dispatch them here anyway —
        // Pie / Heatmap have their own branches above; Line / Scatter /
        // Bar / Area share this fallthrough).
        let stackedProps = if spec.Stacked then [ "stacked" ==> true ] else []

        spec.YFields
        |> List.map (fun yKey ->
            createObj (
                [ "type" ==> seriesType
                  "xKey" ==> spec.XField
                  "yKey" ==> yKey
                  "yName" ==> yKey
                  "listeners" ==> listeners ]
                @ stackedProps
            ))
        |> List.toArray

// ─── Ready-handle ref (Phase 1594) ──────────────────────────────────
//
// AG Charts has no options-level ready event — the raw component handle is
// reached through React's callback ref — and React re-invokes a callback ref
// whenever its IDENTITY changes. The adapter builds fresh props on every
// render, so a closure written inline would be detached and re-attached each
// time, re-entering the host's hook on every re-render: exactly what the
// contract forbids. The callback is therefore cached per node id, which gives
// it a stable identity across renders; the cached cell carries the CURRENT
// hook, so a host that swaps its options is never served a stale closure.
// React hands the callback `null` on unmount, which drops the entry and
// re-arms — a remount is a new chart instance and must fire again.

type private ReadyRef =
    { mutable Hook: (obj -> unit) option
      mutable Fired: bool
      mutable Callback: obj -> unit }

let private readyRefs = System.Collections.Generic.Dictionary<string, ReadyRef>()

let private readyRef (nodeId: string) (hook: obj -> unit) : obj -> unit =
    match readyRefs.TryGetValue nodeId with
    | true, existing ->
        existing.Hook <- Some hook
        existing.Callback
    | _ ->
        let cell =
            { Hook = Some hook
              Fired = false
              Callback = ignore }

        cell.Callback <-
            fun handle ->
                if isNull handle then
                    cell.Fired <- false
                    readyRefs.Remove nodeId |> ignore
                elif not cell.Fired then
                    cell.Fired <- true
                    cell.Hook |> Option.iter (fun h -> h handle)

        readyRefs[nodeId] <- cell
        cell.Callback

// ─── Top-level Chart render ─────────────────────────────────────────

/// `renderChart`, plus the Phase 1594 raw-handle escape valve: `onReady`, when
/// the host supplied one, is invoked once with the mounted `ag-charts-react`
/// component handle (whose `chart` member is the library's own chart
/// instance).
///
/// The hook is a PARAMETER and never a member of `ChartSpec` — it is supplied
/// by the host when it constructs the adapter ([AgAdapter.fs](AgAdapter.fs)),
/// so no decoded tree can carry it, reach it or trigger it. That is a
/// structural guarantee rather than the weaker "a callback cannot cross the
/// wire" argument `ChartSpec.OnPointClick` rests on: it is not a member of any
/// wire type, so there is nothing for a decoder to fill.
///
/// The handle is passed on exactly as React yields it, unwrapped and
/// unvalidated; if the component does not accept a ref the hook simply never
/// fires, which fails closed. See the escape-hatch inventory's Hatch 14.
let renderChartWithReady<'Msg>
    (onReady: (obj -> unit) option)
    (spec: ChartSpec<'Msg>)
    (context: VisAdapter.VisualisationContext<'Msg>)
    : ReactElement option =
    ensureChartsModulesRegistered ()

    // State-slot dispatch mirrors AgGridAdapter — short-circuit to
    // OnLoading / OnError when the binding hasn't resolved. OnEmpty fires
    // once the row sequence resolves to an empty seq.
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
            let series = buildSeries context.RunAction context.NodeId spec

            let titleObj =
                match spec.Title with
                | Some t -> createObj [ "text" ==> textOf t; "enabled" ==> true ]
                | None -> createObj [ "enabled" ==> false ]

            let options =
                createObj
                    [ "data" ==> List.toArray rows
                      "series" ==> series
                      "title" ==> titleObj
                      "legend" ==> createObj [ "enabled" ==> true; "position" ==> "bottom" ] ]

            // Phase 1594 — the escape valve. Attached only when the host asked
            // for it, so a composition that supplied no hook emits exactly the
            // props it emitted before.
            let readyProps =
                match onReady with
                | Some hook -> [ "ref" ==> readyRef context.NodeId hook ]
                | None -> []

            let chartProps = createObj ([ "options" ==> options ] @ readyProps)

            let chartElement =
                ReactLegacy.createElement (unbox<ReactElement> agCharts, chartProps)

            // Wrap to give AG Charts a sized container — the chart auto-sizes
            // to its parent, so the wrapper has to declare a usable size.
            Some(
                Html.div
                    [ prop.className "fuaran-chart"
                      prop.style [ style.width (length.percent 100); style.height 320 ]
                      prop.children [ chartElement ] ]
            )

/// `renderChartWithReady` with no ready hook — the shape every caller had
/// before Phase 1594, kept so the addition is additive rather than a signature
/// change.
let renderChart<'Msg> (spec: ChartSpec<'Msg>) (context: VisAdapter.VisualisationContext<'Msg>) : ReactElement option =
    renderChartWithReady None spec context
