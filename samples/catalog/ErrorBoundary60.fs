module Fuaran.Samples.Catalog.ErrorBoundary60

// ============================================================================
//  Render-time error boundaries + per-node fallback catalog page.
//
//  Three demonstration cards exercise the graceful-degradation contract:
//
//   - **Per-node guard** — a `Dashboard` with mixed children: a healthy
//     Metric, a deliberately-throwing `Custom`, and another healthy Metric.
//     The throwing leaf renders a `fuaran-node-fallback` placeholder
//     (carrying `data-fuaran-render-failed`); the siblings stay live.
//
//   - **Error boundary (caught)** — a `Fuaran.errorBoundary` wraps a
//     throwing-Custom child + a friendly fallback Callout. The
//     boundary's catch suspends the per-node guard for the child
//     subtree (so the throw propagates up); the Fallback Node renders
//     in place of the whole child.
//
//   - **Error boundary (clean path)** — a boundary whose child renders
//     normally. The boundary is structurally inert when no throw fires;
//     the page renders exactly what the child describes. Pinned so
//     authors see the "no-cost when clean" shape alongside the catch
//     paths.
//
//  Catalog scope note. The telemetry surface
//  (`IFuaranTelemetrySink.RecordRenderFailure`) is asserted by the
//  `.NET`-side `Fuaran.UI.Tests/ErrorBoundaryTests.fs` Expecto suite —
//  not here. The catalog .fsproj deliberately avoids
//  `Fuaran.UI.Telemetry.Abstractions` to keep its Fable-transpiled
//  package surface narrow (same rationale that keeps
//  `Fuaran.UI.OpStream.Abstractions` out — see the catalog `.fsproj`
//  comment). This axis captures the renderer's `IFuaranRuntime.Warn`
//  output instead so the visual demo still has a structured failure
//  surface; the telemetry-sink surface is unit-tested under .NET.
//
//  Page URL: `?error-boundary-60=1`.
// ============================================================================

open Elmish
open Feliz
open Fuaran.UI
open Fuaran.Core
open Fuaran.UI.Types
open Fuaran.UI.Renderer

// ─── Model ─────────────────────────────────────────────────────────────────

type Model = { Warnings: string list }

type Msg = WarningCaptured of string

let init () : Model * Cmd<Msg> = { Warnings = [] }, Cmd.none

let update (msg: Msg) (model: Model) : Model * Cmd<Msg> =
    match msg with
    | WarningCaptured w ->
        { model with
            Warnings = w :: model.Warnings },
        Cmd.none

// ─── Capturing runtime ─────────────────────────────────────────────────────
//
// Wraps the diagnostic runtime, registering a deliberately-throwing
// Custom renderer + relaying `Warn` calls into the Elmish dispatch so
// the warnings panel below the cards can render them. Mirrors the
// `BoundedEscape70.fs` capturing-runtime pattern.

let private throwingModuleId = "error-boundary-catalog"
let private throwingComponentId = "ThrowingLeaf"
let private throwingMessage = "catalog deliberate throw"

let private throwingRenderer (_props: Map<string, JVal>) : ReactElement = failwith throwingMessage

type private CapturingRuntime(dispatch: Msg -> unit, registry: Runtime.CustomRendererRegistry) =
    interface Runtime.IFuaranRuntime with
        member _.Call(endpoint, onResult) =
            Runtime.diagnostic.Call(endpoint, onResult)

        member _.Notify(channel, payload) =
            Runtime.diagnostic.Notify(channel, payload)

        member _.Navigate(route) = Runtime.diagnostic.Navigate(route)
        member _.SetState(key, value) = Runtime.diagnostic.SetState(key, value)

        member _.InvokeAiTool(toolName, args) =
            Runtime.diagnostic.InvokeAiTool(toolName, args)

        member _.WriteToClipboard(text) =
            Runtime.diagnostic.WriteToClipboard(text)

        member _.ReadFileBody(file, encoding, onRead) =
            Runtime.diagnostic.ReadFileBody(file, encoding, onRead)

        member _.LayoutObserver = Runtime.diagnostic.LayoutObserver

        member _.Warn(message: string) = dispatch (WarningCaptured message)

        member _.TryRenderCustom(moduleId, componentId, props) =
            registry.TryRender(moduleId, componentId, props)

        member _.TryGetCustomRenderer(moduleId, componentId) = registry.TryGet(moduleId, componentId)

        member _.TryRenderCustomInScope(scope, moduleId, componentId, props) =
            registry.TryRenderInScope(scope, moduleId, componentId, props)

        member _.TryGetCustomRendererInScope(scope, moduleId, componentId) =
            registry.TryGetInScope(scope, moduleId, componentId)

        member _.CanDispatch(_) = true
        member _.TryLoadGuest(_) = None

let private wireRuntime (dispatch: Msg -> unit) : Runtime.IFuaranRuntime =
    let registry = Runtime.CustomRendererRegistry()
    registry.Register(throwingModuleId, throwingComponentId, throwingRenderer)
    CapturingRuntime(dispatch, registry) :> Runtime.IFuaranRuntime

// ─── Tree construction ───────────────────────────────────────────────────

let private throwingLeaf (id: string) : Node<Msg> =
    Fuaran.custom id throwingModuleId throwingComponentId Map.empty None []

let private healthyMetric (id: string) (label: string) (value: float) : Node<Msg> =
    Fuaran.metric
        id
        { Defaults.metric with
            Label = TextSource.Literal label
            Value = Binding.Static(Some value) }

let private heading (id: string) (level: int) (text: string) : Node<Msg> =
    Fuaran.heading
        id
        { Defaults.heading with
            Level = level
            Text = TextSource.Literal text }

let private paragraph (id: string) (body: string) : Node<Msg> = Fuaran.markdown id body

let private perNodeGuardCard: Node<Msg> =
    Fuaran.card
        "eb60-per-node-card"
        { Defaults.card with
            Heading = Some(TextSource.Literal "Per-node guard — one bad leaf is isolated")
            Children =
                [ paragraph
                      "eb60-per-node-body"
                      "The middle child throws during render. The renderer's per-node guard substitutes a fallback placeholder for the failing node only — the surrounding Metrics stay live."
                  Fuaran.dashboard
                      "eb60-per-node-dash"
                      { Children =
                          [ healthyMetric "eb60-metric-a" "Healthy Metric A" 142500.0
                            throwingLeaf "eb60-throwing-leaf"
                            healthyMetric "eb60-metric-b" "Healthy Metric B" 98300.0 ] } ] }

let private friendlyFallback: Node<Msg> =
    Fuaran.callout
        "eb60-fallback-callout"
        { Defaults.callout with
            Tone = ToneVariant.Warning
            Heading = Some(TextSource.Literal "This section couldn't render")
            Body =
                TextSource.Literal
                    "The author-supplied fallback subtree rendered instead. Real consumers would offer a retry, link to docs, or surface a captured error id."
            Icon = Some "alert"
            Dismissable = false }

let private boundaryCaughtCard: Node<Msg> =
    Fuaran.card
        "eb60-boundary-caught-card"
        { Defaults.card with
            Heading = Some(TextSource.Literal "ErrorBoundary — catches a failing subtree")
            Children =
                [ paragraph
                      "eb60-boundary-caught-body"
                      "The boundary's child throws. The boundary's catch suspends the per-node guard for the child subtree so the throw propagates up; the Fallback Node renders in place of the whole child."
                  Fuaran.errorBoundary
                      "eb60-boundary-caught"
                      { Child = throwingLeaf "eb60-boundary-caught-child"
                        Fallback = friendlyFallback } ] }

let private boundaryCleanCard: Node<Msg> =
    Fuaran.card
        "eb60-boundary-clean-card"
        { Defaults.card with
            Heading = Some(TextSource.Literal "ErrorBoundary — clean path is structurally inert")
            Children =
                [ paragraph
                      "eb60-boundary-clean-body"
                      "When the child renders without throwing, the boundary contributes nothing visible — the page renders exactly what the child describes."
                  Fuaran.errorBoundary
                      "eb60-boundary-clean"
                      { Child =
                          healthyMetric
                              "eb60-boundary-clean-child"
                              "Boundary-wrapped Metric (renders normally)"
                              312000.0
                        Fallback = friendlyFallback } ] }

let private rootDashboard: Node<Msg> =
    Fuaran.dashboard
        "eb60-root"
        { Children =
            [ heading "eb60-title" 1 "Render-time error boundaries"
              perNodeGuardCard
              boundaryCaughtCard
              boundaryCleanCard ] }

// ─── View ──────────────────────────────────────────────────────────────────

let view (model: Model) (dispatch: Msg -> unit) : ReactElement =
    let runtime = wireRuntime dispatch

    let ctx: Render.RenderContext<Msg> =
        { Sources = BindingResolver.empty
          Runtime = runtime
          VisAdapter = VisAdapter.noOp<Msg>
          Dispatch = dispatch
          TelemetrySink = None
          InErrorBoundary = false
          Fragments = Map.empty
          ExpandingFragments = Set.empty
          Scope = None
          SessionContext = Map.empty
          // Phase 889 — no user-action recording in the samples.
          ActionSink = None
          CurrentNodeId = None }

    React.Fragment
        [ Render.themeStyleElement Defaults.theme
          Html.div
              [ prop.id "error-boundary-60-page"
                prop.style [ style.padding 24 ]
                prop.children
                    [ Render.render ctx rootDashboard
                      Html.section
                          [ prop.custom ("data-testid", "eb60-captured-warnings")
                            prop.style [ style.marginTop 32; style.fontFamily "monospace" ]
                            prop.children
                                [ Html.h2 [ prop.text "Captured runtime warnings" ]
                                  if model.Warnings.IsEmpty then
                                      Html.p [ prop.text "(none yet)" ]
                                  else
                                      Html.ol
                                          [ prop.children
                                                [ for w in List.rev model.Warnings -> Html.li [ prop.text w ] ] ] ] ] ] ] ]
