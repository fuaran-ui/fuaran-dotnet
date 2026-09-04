module Fuaran.Samples.Catalog.Tabs69

// ============================================================================
//  Explicit headers + ARIA / keyboard navigation + typed-tag
//  overlay catalog page.
//
//  Renders three tabs that exercise the full tab surface:
//
//   - **Overview** — typed Fuaran body (Markdown).
//   - **Detail (Custom)** — NodeKind.Custom body validating the
//     "Custom escape works for non-Fuaran tab bodies" claim. The
//     runtime registers a renderer for (`tabs-69`, `DetailPane`)
//     that emits a Feliz-native panel — the typed Fuaran tree wraps
//     a Feliz body via Custom + RegisterCustomRenderer.
//   - **Audit (disabled)** — header.Disabled = Some(Static true) so the
//     Playwright spec can assert the disabled-tab is skipped during
//     keyboard navigation.
//
//  Model-side state is a typed DU (`ResultsTab = Overview | Detail | Audit`);
//  the `TabTags` / `ActiveTag` / `OnSelectTag` overlay maps it to / from
//  the wire `string` tag with `tagOf` / `tabOf` helpers so the model
//  doesn't carry an integer-indirection step.
//
//  Page URL: `?tabs-69=1`. Playwright spec at
//  `snapshot/tabs-69.spec.mts` probes the ARIA emission + keyboard
//  navigation + disabled-tab skip + tag-overlay round-trip.
// ============================================================================

open Elmish
open Feliz
open Fuaran.UI
open Fuaran.Core
open Fuaran.UI.Types
open Fuaran.UI.Renderer

// ─── Model ─────────────────────────────────────────────────────────────────

[<RequireQualifiedAccess>]
type ResultsTab =
    | Overview
    | Detail
    | Audit

type Model = { Active: ResultsTab }

type Msg = SetActive of ResultsTab

let init () : Model * Cmd<Msg> =
    { Active = ResultsTab.Overview }, Cmd.none

let update (msg: Msg) (model: Model) : Model * Cmd<Msg> =
    match msg with
    | SetActive tab -> { model with Active = tab }, Cmd.none

// ─── Typed DU <-> wire-string tag bridge ───────────────────────────────────

let private tagOf (tab: ResultsTab) : string =
    match tab with
    | ResultsTab.Overview -> "overview"
    | ResultsTab.Detail -> "detail"
    | ResultsTab.Audit -> "audit"

let private tabOf (tag: string) : ResultsTab =
    match tag with
    | "overview" -> ResultsTab.Overview
    | "detail" -> ResultsTab.Detail
    | "audit" -> ResultsTab.Audit
    | _ -> ResultsTab.Overview

// ─── Custom renderer for the Detail tab ────────────────────────────────────
//
// IFuaranRuntime.RegisterCustomRenderer takes
// `Map<string, JVal> -> ReactElement`. The Detail tab's body is a
// Feliz fragment registered under (`tabs-69`, `DetailPane`) — exercising
// the NodeKind.Custom escape for a non-typed-Fuaran body.

let private detailPaneRenderer (_props: Map<string, JVal>) : ReactElement =
    Html.div
        [ prop.custom ("data-testid", "tabs-69-detail-pane")
          prop.children
              [ Html.h3 [ prop.text "Detail (Custom-wrapped Feliz body)" ]
                Html.p
                    [ prop.text
                          "This panel is a Feliz fragment registered via IFuaranRuntime.RegisterCustomRenderer — exercising the NodeKind.Custom escape." ] ] ]

// ─── Tab body factories ────────────────────────────────────────────────────

let private overviewTab: Node<Msg> =
    Fuaran.markdown "tabs-69-overview-body" "Overview pane content (typed Fuaran)."

let private detailTab: Node<Msg> =
    // NodeKind.Custom — the runtime resolves (`tabs-69`, `DetailPane`) to
    // detailPaneRenderer at render time. Mirrors the buildNode shape used by
    // Fuaran.UI/Fuaran.fs's smart constructors.
    { Id = "tabs-69-detail-body"
      Kind =
        NodeKind.Custom(
            { ModuleId = "tabs-69"
              ComponentId = "DetailPane"
              Props = Map.empty
              ContentHash = None
              ExposedNodeIds = None }
        )
      State = Option.None
      Style = Option.None
      Accessibility = Option.None
      Motion = Defaults.Motion.none
      ExtraAttributes = Option.None
      Tooltip = None }

let private auditTab: Node<Msg> =
    Fuaran.markdown "tabs-69-audit-body" "Audit pane content (disabled)."

// ─── The tabs node ─────────────────────────────────────────────────────────

let private tabsNode (model: Model) : Node<Msg> =
    Fuaran.tabsTagged
        "tabs-69"
        { Defaults.tabs<Msg> with
            Children = [ overviewTab; detailTab; auditTab ]
            TabHeaders =
                Some
                    [ { Defaults.tabHeader with
                          Label = TextSource.Literal "Overview" }
                      { Defaults.tabHeader with
                          Label = TextSource.Literal "Detail" }
                      { Defaults.tabHeader with
                          Label = TextSource.Literal "Audit"
                          Disabled = Some(Binding.Static(Some true)) } ]
            TabTags = Some [ "overview"; "detail"; "audit" ]
            ActiveTag = Some(Binding.Static(Some(tagOf model.Active)))
            // Phase 1152 — through the documented `Action.dispatch` helper
            // rather than the generated case. The case now carries the IDL's
            // `inProcessOnly` marking, and this sample IS an authoring site, so
            // the honest answer to the warning is to take the route whose doc
            // comment states the constraint — not to suppress it. Behaviour is
            // identical: `dispatch` is `Action.Dispatch` with the paragraph
            // attached, and this catalog page is a full-Fable in-process host
            // where the case is correct.
            OnSelectTag = Some(fun tag -> Action.dispatch (SetActive(tabOf tag))) }

// ─── Runtime wiring — register the Custom renderer ─────────────────────────
//
// `wireRuntime` is called once per render but `MutableRuntime` registry is
// idempotent for repeated registrations of the same (moduleId, componentId)
// pair (later registration wins). Avoiding the per-render allocation would
// take a state-hook or a memo — out of scope for the fixture.

let private wireRuntime () : Runtime.IFuaranRuntime =
    let runtime = Runtime.MutableRuntime()
    runtime.RegisterCustomRenderer("tabs-69", "DetailPane", detailPaneRenderer)
    runtime :> Runtime.IFuaranRuntime

// ─── View ──────────────────────────────────────────────────────────────────

let view (model: Model) (dispatch: Msg -> unit) : ReactElement =
    let runtime = wireRuntime ()

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
          CurrentNodeId = None
          // Phase 1026 — a HAND-AUTHORED tree, where the author is the trust
          // boundary, so the permissive posture is correct and is reached BY NAME.
          // A host rendering a DECODED tree must not copy this line.
          EgressPolicy = Sanitize.permissiveEgress
          // Phase 1117 — no upload sink: this surface performs no uploads.
          UploadSink = None }

    React.Fragment
        [ Render.themeStyleElement Defaults.theme
          Html.div
              [ prop.id "tabs-69-page"
                prop.style [ style.padding 24 ]
                prop.children
                    [ Html.h1 [ prop.text "Explicit headers + ARIA + tag overlay" ]
                      Html.p
                          [ prop.text
                                "Snapshot harness fixture. Three tabs: Overview (typed Fuaran), Detail (NodeKind.Custom-wrapped Feliz), Audit (disabled). Tab bar is keyboard-navigable; Playwright spec asserts ARIA attributes + arrow-key behaviour + disabled-tab skip + tag-overlay round-trip." ]
                      Html.div
                          [ prop.custom ("data-testid", "tabs-69-active-tag-mirror")
                            prop.text (sprintf "Active tag: %s" (tagOf model.Active)) ]
                      Render.render ctx (tabsNode model) ] ] ]
