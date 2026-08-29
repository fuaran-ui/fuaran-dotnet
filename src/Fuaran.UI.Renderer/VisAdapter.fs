module Fuaran.UI.Renderer.VisAdapter

// ============================================================================
//  Fuaran — visualisation adapter seam (session 3b)
//
//  `NodeKind.DataGrid` and `NodeKind.Chart` are the two `Visualisation` kinds with
//  third-party-library implementations whose authoring shape doesn't fit
//  the simple Feliz render path. The renderer ships a simple-HTML-table
//  fallback for Grid + a labelled-placeholder for Chart (per
//  `Render.renderGrid` / `Render.renderChart`); consumers wanting full
//  AG Grid / AG Charts integration supply an `IVisualisationAdapter<'Msg>`
//  that the renderer consults before the fallback.
//
//  Why the seam vs platform bindings inlined here?
//  ──────────────────────────────────────────────
//  Fuaran.UI.Renderer's standalone-posture mandate (workspace CLAUDE.md FGP 2 +
//  Fuaran CLAUDE.md "Standalone posture mandate") forbids external platform
//  package references in this project. The AG Grid + AG Charts adapters that ship
//  with Fuaran.UI.Renderer reach the same
//  ag-grid-react / ag-charts-react npm packages a platform's bindings use, via
//  `Fable.Core.JsInterop` — no external platform .NET project reference. The blessed
//  platform-aware integration tier remains the platform adapter (per FGP 6)
//  and is separate from this in-tree adapter.
//
//  Context bundle (added 2026-05-26 for the AG Grid / AG Charts adapter)
//  ──────────────────────────────────────────────────────────────────
//  `IVisualisationAdapter` methods take a `VisualisationContext<'Msg>` that
//  bundles the renderer-side dependencies a concrete adapter needs to
//  resolve `spec.Source`, recursively render `OnLoading` / `OnEmpty` /
//  `OnError` state-slot Nodes (and `CellKindErased.Custom` cell-bodies),
//  and dispatch typed `Action<'Msg>` events. Without these the adapter
//  would either duplicate the binding-resolution logic or force the
//  renderer to pre-resolve rows on the adapter's behalf — either path
//  splits state-slot handling across two files. Threading the same
//  fields the renderer's `RenderContext` already carries keeps the
//  contract self-contained.
//
//  AG Grid event mapping (for the adapter implementer)
//  ──────────────────────────────────────────────────
//  The adapter is responsible for translating AG Grid's onCellValueChanged /
//  onRowClicked / onSortChanged / onFilterChanged events into typed
//  `Action<'Msg>` via the renderer-supplied `RunAction` callback. For each
//  GridSpec column whose Kind is:
//    - CellKindErased.Editable f → wire AG Grid's `valueSetter` to
//      `runAction (f (rowData, newCellValue))`. AG Grid's
//      IValueChangedParams<'row,'value> carries `newValue` + `data` (the
//      row obj) which map directly. Return `false` from `valueSetter` so
//      AG Grid doesn't mutate the row obj itself — the consumer's Elmish
//      update owns row state.
//    - CellKindErased.Checkbox(get, onToggle) → render a custom
//      `cellRenderer` with an `<input type=checkbox>` and wire its
//      change event to `runAction (onToggle (row, newValue))`.
//    - CellKindErased.Button(label, onClick) → render a custom button
//      `cellRenderer` whose click handler calls `runAction (onClick row)`.
//      Use `event.stopPropagation()` so the per-row click doesn't fire.
//    - CellKindErased.ButtonGroup buttons → emit one button per entry,
//      same shape as Button.
//    - CellKindErased.Link(href, label) → custom `cellRenderer` with the
//      resolved href + label; no Action wiring.
//    - CellKindErased.Pill / Progress → custom `cellRenderer` that mirrors
//      the simple-HTML fallback's per-row shape; no Action wiring.
//    - CellKindErased.Text / Numeric / Date → no `cellRenderer`; AG Grid's
//      default cell display is fine once `valueFormatter` is wired.
//    - CellKindErased.Custom render → render the nested Fuaran Node inside
//      AG Grid's `cellRenderer` shape, recursing back through the
//      `RecurseRender` callback the context supplies.
//  `GridSpec.OnRowClick` wires to AG Grid's `onRowClicked` event with the
//  full row obj.
//
//  AG Charts event mapping
//  ──────────────────────
//  AG Charts series `listeners.nodeClick` event carries `datum: obj` →
//  `Render.chartPointSelected`, which dispatches `ChartSpec.OnPointClick` when
//  the author supplied one and otherwise publishes the datum under the node's
//  own id (the Phase 933 default). The listener is attached UNCONDITIONALLY:
//  `OnPointClick` is `None` on every DECODED chart, because a callback cannot
//  cross the wire, so attaching only in the `Some` case left the affordance
//  inert on exactly the trees the default exists for. Series kind translation:
//    - ChartKind.Line / Bar / Area / Scatter → AG Charts `type: "line"` /
//      `"bar"` / `"area"` / `"scatter"`.
//    - ChartKind.Pie → AG Charts `type: "pie"`.
//  `ChartSpec.YFields` maps to one AG Charts series per field with
//  `xKey = ChartSpec.XField`, `yKey = field`.
// ============================================================================

open Feliz
open Fuaran.UI.Types

/// Bundle of renderer-side dependencies an `IVisualisationAdapter` needs:
/// binding sources (for `spec.Source` + state-key resolution), the node's
/// `StateBehaviour` (for `OnLoading` / `OnEmpty` / `OnError` slot dispatch),
/// a recurse-render callback (to project state-slot Nodes + `CellKindErased.Custom`
/// cell bodies through the renderer), and a typed-action dispatch.
///
/// Threading the same fields the renderer's private `RenderContext` already
/// carries keeps the adapter contract self-contained without exposing the
/// `RenderContext` record itself (which would force a cycle between this
/// file and `Render.fs`).
type VisualisationContext<'Msg> =
    {
        Sources: BindingResolver.BindingSources
        State: StateBehaviour<'Msg>
        RecurseRender: Node<'Msg> -> ReactElement
        RunAction: Action<'Msg> -> unit
        /// The id of the node being rendered — the one field an adapter cannot
        /// recover from the spec, because a `GridSpec` / `ChartSpec` does not
        /// carry its own node's id.
        ///
        /// It is here because the SELECTION DEFAULTS need it. A grid whose
        /// `OnRowClick` is `None` (Phase 427) and a chart whose `OnPointClick`
        /// is `None` (Phase 933) publish the clicked datum to the
        /// `SelectionStore` under the NODE's own id, which is what makes
        /// decoded master-detail work with no host code: a callback cannot
        /// cross the wire, so the slot always arrives `None` on a decoded tree.
        /// Without this field an adapter could dispatch the closure case and
        /// nothing else, so the whole affordance was inert on the adapter path
        /// while working on the first-party one.
        NodeId: string
    }

/// Adapter contract for the two Visualisation kinds whose author surface
/// doesn't fit the simple Feliz fallback (Grid, Chart). The renderer
/// consults the adapter first; if either method returns `None`, the
/// renderer's built-in fallback runs.
///
/// The adapter handles its own state-slot dispatch — it has `context.State`
/// + `context.RecurseRender` for the loading / empty / error branches, and
/// receives `spec.Source` unresolved so it can decide when to short-circuit
/// to a state slot vs commit to a real grid / chart render.
type IVisualisationAdapter<'Msg> =
    /// Render a Fuaran Grid via the consumer's grid library. Return `None`
    /// to fall back to the renderer's simple-HTML-table.
    abstract RenderGrid: spec: GridSpec<'Msg> * context: VisualisationContext<'Msg> -> ReactElement option

    /// Render a Fuaran Chart via the consumer's chart library. Return `None`
    /// to fall back to the renderer's labelled placeholder.
    abstract RenderChart: spec: ChartSpec<'Msg> * context: VisualisationContext<'Msg> -> ReactElement option

/// No-op adapter — both methods return `None`, deferring to the renderer's
/// built-in fallbacks. Used by default when the caller does not supply a
/// real adapter (e.g. the demo's first-mount path, the test suite).
type NoOpVisualisationAdapter<'Msg>() =
    interface IVisualisationAdapter<'Msg> with
        member _.RenderGrid(_, _) = None
        member _.RenderChart(_, _) = None

/// Shared instance of the no-op adapter — saves callers from allocating one.
let noOp<'Msg> : IVisualisationAdapter<'Msg> =
    NoOpVisualisationAdapter<'Msg>() :> IVisualisationAdapter<'Msg>
