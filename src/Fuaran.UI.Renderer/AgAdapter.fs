module Fuaran.UI.Renderer.AgAdapter

// ============================================================================
//  Fuaran — combined AG Grid + AG Charts adapter
//
//  Exposes a single `IVisualisationAdapter<'Msg>` that consumers can drop
//  into `RenderContext.VisAdapter`. Both Grid and Chart adapter halves are
//  available; the adapter delegates to [`AgGridAdapter`](AgGridAdapter.fs)
//  and [`AgChartAdapter`](AgChartAdapter.fs) per `IVisualisationAdapter`'s
//  contract.
//
//  Standalone-posture note: the adapter pulls in `ag-grid-react`,
//  `ag-grid-community`, `ag-charts-react`, and `ag-charts-community` at the
//  npm layer. Consumers wanting the simple-HTML fallback (no third-party
//  grid / chart deps) should keep `VisAdapter.noOp<'Msg>` instead of this
//  adapter — the renderer's fallback path runs when the adapter's methods
//  return `None`, which `noOp` always does.
// ============================================================================

open Feliz
open Fuaran.UI.Types

/// Combined AG Grid + AG Charts implementation of `IVisualisationAdapter<'Msg>`.
/// Delegates `RenderGrid` to [`AgGridAdapter.renderGrid`](AgGridAdapter.fs) and
/// `RenderChart` to [`AgChartAdapter.renderChart`](AgChartAdapter.fs).
type AgVisualisationAdapter<'Msg>() =
    interface VisAdapter.IVisualisationAdapter<'Msg> with
        member _.RenderGrid(spec, context) = AgGridAdapter.renderGrid spec context

        member _.RenderChart(spec, context) = AgChartAdapter.renderChart spec context

/// Shared instance of the combined AG adapter — saves callers from
/// allocating one per render.
let adapter<'Msg> : VisAdapter.IVisualisationAdapter<'Msg> =
    AgVisualisationAdapter<'Msg>() :> VisAdapter.IVisualisationAdapter<'Msg>
