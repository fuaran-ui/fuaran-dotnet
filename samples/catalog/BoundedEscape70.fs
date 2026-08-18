module Fuaran.Samples.Catalog.BoundedEscape70

// ============================================================================
//  Custom bounded-escape (contentHash + exposedNodeIds) catalog
//  page.
//
//  Renders four `NodeKind.Custom` cards exercising the verification matrix:
//
//   - **Green path** — declared `contentHash` matches the registered
//     renderer's hash; declared `exposedNodeIds` are emitted as
//     `data-fuaran-node-id` attributes; layout observer + AI introspection
//     can address them.
//
//   - **Strict mismatch** — declared hash differs from registered, with
//     `Strictness = StrictReplay`. Renderer warns through
//     `IFuaranRuntime.Warn` and routes through the node's `OnError` slot.
//
//   - **Advisory mismatch** — declared hash differs from registered, with
//     `Strictness = AdvisoryWarning`. Renderer warns + renders the body
//     anyway.
//
//   - **Exposed-ids missing** — declared `exposedNodeIds` includes an id
//     the registered renderer does NOT emit. Post-mount DOM walk fires a
//     warning; the build-time validator's FUARAN053 catches the case
//     earlier when (moduleId, componentId) has no matching
//     `RegisterCustomRenderer` call in the project.
//
//  Page URL: `?bounded-escape-70=1`. Playwright spec (snapshot/) probes
//  the rendered DOM + warn channel.
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

// ─── Registered renderers ──────────────────────────────────────────────────
//
// The "green" renderer emits two interior `data-fuaran-node-id` elements
// that match the declared `exposedNodeIds`. The "missing" renderer emits
// only one — the second declared id has no corresponding element, so the
// post-mount DOM walk fires a warning.

let private greenRenderer (_props: Map<string, JVal>) : ReactElement =
    Html.div
        [ prop.className "fuaran-be70-green"
          prop.children
              [ Html.div
                    [ prop.custom ("data-fuaran-node-id", "be70-green-segment-a")
                      prop.text "Green segment A (exposed-id emitted)" ]
                Html.div
                    [ prop.custom ("data-fuaran-node-id", "be70-green-segment-b")
                      prop.text "Green segment B (exposed-id emitted)" ] ] ]

let private mismatchRenderer (_props: Map<string, JVal>) : ReactElement =
    Html.div
        [ prop.className "fuaran-be70-mismatch-body"
          prop.text "This is the registered renderer's body — the tree's declared contentHash does not match." ]

let private missingExposedIdsRenderer (_props: Map<string, JVal>) : ReactElement =
    Html.div
        [ prop.className "fuaran-be70-missing-body"
          prop.children
              [ Html.div
                    [ prop.custom ("data-fuaran-node-id", "be70-missing-segment-only")
                      prop.text "Only one exposed id is emitted; the tree declares two." ] ] ]

// ─── Hash literals — for the catalog, hand-set rather than computed ─────
//
// A real consumer pipeline would compute these at build time (FCS-driven
// SHA-256 over the renderer's source). The catalog fixture uses literal
// strings so the green/red branches are statically distinguishable.

let private greenHash: ContentHash =
    { Algorithm = "SHA256"
      Hash = "GREEN_HASH_MATCHES_REGISTRY"
      Strictness = HashStrictness.StrictReplay }

let private strictMismatchHash: ContentHash =
    { Algorithm = "SHA256"
      Hash = "STRICT_MISMATCH_TREE_HASH"
      Strictness = HashStrictness.StrictReplay }

let private advisoryMismatchHash: ContentHash =
    { Algorithm = "SHA256"
      Hash = "ADVISORY_MISMATCH_TREE_HASH"
      Strictness = HashStrictness.AdvisoryWarning }

let private mismatchRegistryHash: ContentHash =
    { Algorithm = "SHA256"
      Hash = "REGISTRY_HASH_DIFFERS"
      Strictness = HashStrictness.StrictReplay }

// ─── Tree construction ─────────────────────────────────────────────────────

let private onErrorSlot (payload: ErrorPayload) : Node<Msg> =
    Fuaran.callout
        "be70-onerror-slot"
        { Defaults.callout with
            Tone = ToneVariant.Critical
            Heading = Some(TextSource.Literal "Hash mismatch — OnError slot rendered")
            Body = TextSource.Literal payload.Message
            Icon = Some "alert"
            Dismissable = false }

let private greenCard: Node<Msg> =
    Fuaran.custom
        "be70-green"
        "bounded-escape-70"
        "GreenPanel"
        Map.empty
        (Some greenHash)
        [ NodeId "be70-green-segment-a"; NodeId "be70-green-segment-b" ]

let private strictMismatchCard: Node<Msg> =
    Fuaran.custom "be70-strict-mismatch" "bounded-escape-70" "MismatchPanel" Map.empty (Some strictMismatchHash) []
    |> Node.onError onErrorSlot

let private advisoryMismatchCard: Node<Msg> =
    Fuaran.custom "be70-advisory-mismatch" "bounded-escape-70" "MismatchPanel" Map.empty (Some advisoryMismatchHash) []

let private missingExposedIdsCard: Node<Msg> =
    Fuaran.custom
        "be70-missing-exposed"
        "bounded-escape-70"
        "MissingExposedIdsPanel"
        Map.empty
        None
        [ NodeId "be70-missing-segment-only"
          NodeId "be70-missing-segment-declared-but-absent" ]

let private rootDashboard: Node<Msg> =
    Fuaran.dashboard
        "be70-root"
        { Children =
            [ Fuaran.heading
                  "be70-heading"
                  { Defaults.heading with
                      Level = 1
                      Text = TextSource.Literal "Custom bounded escape" }
              Fuaran.markdown
                  "be70-intro"
                  "Four Custom variants exercising the contentHash + exposedNodeIds verification matrix. The browser console (or the captured-warnings panel below) shows the hash-mismatch + exposed-id-missing warnings as they fire."
              greenCard
              strictMismatchCard
              advisoryMismatchCard
              missingExposedIdsCard ] }

// ─── Runtime wiring ────────────────────────────────────────────────────────

/// Capture the renderer's `Warn` calls so the page can display them inline
/// for the snapshot harness + manual inspection. Hands off to a model-side
/// dispatch via the Elmish `dispatch` closure.
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

        member _.Warn(message) =
            // Surface warnings into the model so the page renders them
            // alongside the cards. Also log to console for the dev-tools
            // observer + Playwright spec.
            dispatch (WarningCaptured message)
            Runtime.diagnostic.Warn(message)

        member _.LayoutObserver = None

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

    // Green path: register hash matches tree's greenHash.
    registry.Register("bounded-escape-70", "GreenPanel", greenRenderer, greenHash)

    // Mismatch path: register hash differs from tree's strict + advisory
    // tree-declared hashes; both will fire Warn channel with structured
    // payload.
    registry.Register("bounded-escape-70", "MismatchPanel", mismatchRenderer, mismatchRegistryHash)

    // Missing-exposed-ids path: register with no hash, so the renderer
    // skips hash verification; the DOM walk then catches the missing
    // exposed-id.
    registry.Register("bounded-escape-70", "MissingExposedIdsPanel", missingExposedIdsRenderer)

    CapturingRuntime(dispatch, registry) :> Runtime.IFuaranRuntime

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
              [ prop.id "bounded-escape-70-page"
                prop.style [ style.padding 24 ]
                prop.children
                    [ Render.render ctx rootDashboard
                      Html.section
                          [ prop.custom ("data-testid", "be70-captured-warnings")
                            prop.style [ style.marginTop 32; style.fontFamily "monospace" ]
                            prop.children
                                [ Html.h2 [ prop.text "Captured warn-channel messages" ]
                                  Html.ol
                                      [ prop.children (
                                            model.Warnings |> List.rev |> List.map (fun w -> Html.li [ prop.text w ])
                                        ) ] ] ] ] ] ]
