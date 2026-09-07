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

/// Host-side construction options for the combined AG adapter — the Phase 1594
/// raw-handle escape valve.
///
/// An application that needs the grid or chart library's own API has, until
/// now, had to fork the adapter: the typed path exposes no handle. These hooks
/// close that without widening the wire at all, because they are supplied
/// **when the host constructs the adapter** and are threaded to
/// [`AgGridAdapter`](AgGridAdapter.fs) / [`AgChartAdapter`](AgChartAdapter.fs)
/// as a parameter — never through `GridSpec` / `ChartSpec`.
///
/// **Why the constructor rather than the spec.** `GridSpec.OnRowClick` and
/// `ChartSpec.OnPointClick` are authoring-only for a *behavioural* reason —
/// they always arrive `None` on a decoded tree, because a callback cannot cross
/// the wire. This record is authoring-only for a *structural* one: it is not a
/// member of any wire-decoded type, so no emission can name it and no decoder
/// has a slot to fill. "No wire path constructs it" is therefore true by
/// inspection, and the proof is a type-level test rather than a decoder probe
/// (`Fuaran.UI.Tests.AdapterOptionsIsolation`).
///
/// Each hook receives the library's own instance, unwrapped and unvalidated,
/// and is invoked once per mounted instance — never re-entered on re-render.
/// The stack makes no claim about what a host then does with the handle; see
/// Hatch 14 of the escape-hatch inventory.
///
/// ```fsharp
/// // The host keeps the handle; the tree never sees it.
/// let mutable gridApi: obj option = None
///
/// let visAdapter =
///     AgAdapter.adapterWith<Msg>
///         { AgAdapter.defaultOptions with OnGridReady = Some(fun api -> gridApi <- Some api) }
/// // …then: { renderContext with VisAdapter = visAdapter }
/// ```
///
/// `'Msg` carries no field today. It is declared so the record stays in
/// lockstep with the adapter it configures — a hook that needs to speak the
/// composition's message type can be added without renaming the type or
/// breaking a consumer's annotation.
type AgAdapterOptions<'Msg> =
    {
        /// Invoked with AG Grid's own grid API (`GridReadyEvent.api`) once the
        /// grid signals readiness.
        OnGridReady: (obj -> unit) option
        /// Invoked with the mounted `ag-charts-react` component handle, whose
        /// `chart` member is the library's chart instance.
        OnChartReady: (obj -> unit) option
        /// Invoked for **every** visualisation instance this adapter mounts,
        /// grid or chart, immediately after the kind-specific hook — the one
        /// place a host can observe every raw handle without wiring both.
        OnVisReady: (obj -> unit) option
    }

/// No hooks — the adapter's behaviour before Phase 1594, and the base a host
/// copies with `{ defaultOptions with … }`.
let defaultOptions<'Msg> : AgAdapterOptions<'Msg> =
    { OnGridReady = None
      OnChartReady = None
      OnVisReady = None }

/// Kind-specific hook first, then the kind-agnostic one. `None` when neither
/// was supplied, so a host that configured nothing adds no prop at all.
let private combineReady
    (kindSpecific: (obj -> unit) option)
    (kindAgnostic: (obj -> unit) option)
    : (obj -> unit) option =
    match kindSpecific, kindAgnostic with
    | None, None -> None
    | Some f, None -> Some f
    | None, Some g -> Some g
    | Some f, Some g ->
        Some(fun handle ->
            f handle
            g handle)

/// Combined AG Grid + AG Charts implementation of `IVisualisationAdapter<'Msg>`.
/// Delegates `RenderGrid` to [`AgGridAdapter.renderGridWithReady`](AgGridAdapter.fs)
/// and `RenderChart` to [`AgChartAdapter.renderChartWithReady`](AgChartAdapter.fs),
/// threading the host's `OnReady` hooks (Phase 1594) as a parameter.
type AgVisualisationAdapter<'Msg>(options: AgAdapterOptions<'Msg>) =
    /// The hook-free adapter — the constructor every caller had before Phase 1594.
    new() = AgVisualisationAdapter<'Msg>(defaultOptions<'Msg>)

    interface VisAdapter.IVisualisationAdapter<'Msg> with
        member _.RenderGrid(spec, context) =
            AgGridAdapter.renderGridWithReady (combineReady options.OnGridReady options.OnVisReady) spec context

        member _.RenderChart(spec, context) =
            AgChartAdapter.renderChartWithReady (combineReady options.OnChartReady options.OnVisReady) spec context

/// Shared instance of the combined AG adapter — saves callers from
/// allocating one per render. Carries no `OnReady` hooks.
let adapter<'Msg> : VisAdapter.IVisualisationAdapter<'Msg> =
    AgVisualisationAdapter<'Msg>() :> VisAdapter.IVisualisationAdapter<'Msg>

/// The combined AG adapter with host-supplied construction options — the
/// Phase 1594 entry point. Allocate once per composition, not per render.
let adapterWith<'Msg> (options: AgAdapterOptions<'Msg>) : VisAdapter.IVisualisationAdapter<'Msg> =
    AgVisualisationAdapter<'Msg>(options) :> VisAdapter.IVisualisationAdapter<'Msg>
