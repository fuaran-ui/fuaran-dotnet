module Fuaran.Samples.Catalog.Fragments61

// ============================================================================
//  Reusable subtree fragments + named partials catalog page.
//
//  Three demonstration cards exercise the fragment primitive:
//
//   - **Decl-only (zero paint)** — a `Fuaran.fragmentDecl` declared inside
//     a Dashboard with no matching ref. The page shows that the decl
//     contributes no visible output (the body is the *template*, not the
//     rendering); only the surrounding Metrics render.
//
//   - **Single ref expansion + namespaced ids** — a decl whose body
//     contains nodes with bare ids (`btn`, `value`); two refs expand it.
//     The page DOM picks up `data-fuaran-node-id="frag-card.btn"` /
//     `data-fuaran-node-id="frag-card2.btn"` on the namespaced
//     descendants, demonstrating the per-ref uniqueness contract that
//     keeps layout-observer + op-stream-replay machinery addressable
//     across re-use sites.
//
//   - **Cycle guard** — a decl whose body transitively references itself
//     (decl A's body holds a FragmentRef back to A). The renderer's
//     runtime cycle-guard substitutes a labelled placeholder rather than
//     looping forever; the captured-warnings panel below shows the
//     guard's diagnostic emission. (FUARAN058 catches most cycles at
//     build time; this card demonstrates the runtime safety net for
//     anything that escapes the validator.)
//
//  Catalog scope note. The build-time validator findings (FUARAN056 /
//  FUARAN057 / FUARAN058) are asserted by the `.NET`-side validator
//  test suite, not here — same separation as the error-boundary catalog axis
//  vs. ErrorBoundaryTests. This axis is the *visual* demonstration of
//  the rendering contract.
//
//  Page URL: `?fragments-61=1`.
// ============================================================================

open Elmish
open Feliz
open Fuaran.UI
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

// ─── Capturing runtime ────────────────────────────────────────────────────
//
// Wraps the diagnostic runtime, relaying `Warn` calls into the Elmish
// dispatch so the cycle-guard placeholder + unresolved-ref placeholder
// surfaces visibly below the cards. Mirrors the `ErrorBoundary60.fs`
// capturing-runtime pattern.

type private CapturingRuntime(dispatch: Msg -> unit) =
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
            Runtime.diagnostic.TryRenderCustom(moduleId, componentId, props)

        member _.TryGetCustomRenderer(moduleId, componentId) =
            Runtime.diagnostic.TryGetCustomRenderer(moduleId, componentId)

        member _.CanDispatch(action) = Runtime.diagnostic.CanDispatch(action)
        member _.TryLoadGuest(_) = None

let private wireRuntime (dispatch: Msg -> unit) : Runtime.IFuaranRuntime =
    CapturingRuntime(dispatch) :> Runtime.IFuaranRuntime

// ─── Helpers ──────────────────────────────────────────────────────────────

let private heading (id: string) (level: int) (text: string) : Node<Msg> =
    Fuaran.heading
        id
        { Defaults.heading with
            Level = level
            Text = TextSource.Literal text }

let private paragraph (id: string) (body: string) : Node<Msg> = Fuaran.markdown id body

let private metric (id: string) (label: string) (value: float) : Node<Msg> =
    Fuaran.metric
        id
        { Defaults.metric with
            Label = TextSource.Literal label
            Value = Binding.Static value }

let private card (id: string) (title: string) (children: Node<Msg> list) : Node<Msg> =
    Fuaran.card
        id
        { Defaults.card with
            Heading = Some(TextSource.Literal title)
            Children = children }

let private dashboard (id: string) (children: Node<Msg> list) : Node<Msg> =
    Fuaran.dashboard id { Children = children }

let private fragmentDecl (id: string) (name: string) (body: Node<Msg>) : Node<Msg> =
    Fuaran.fragmentDecl
        id
        { Defaults.fragmentDecl with
            Name = FragmentId name
            Body = body }

// ─── Demonstration trees ──────────────────────────────────────────────────

// Card 1 — a decl with no matching ref. Body should NOT render visibly.

let private declOnlyCard: Node<Msg> =
    card
        "frag61-decl-only-card"
        "Decl-only (no ref) — zero paint"
        [ paragraph
              "frag61-decl-only-body"
              "A FragmentDecl with no matching FragmentRef contributes nothing visible. The body below is registered under name 'unused-template' but never referenced; only the surrounding Metrics render."
          dashboard
              "frag61-decl-only-dash"
              [ metric "frag61-decl-only-metric-before" "Renders normally" 100.0
                fragmentDecl
                    "frag61-decl-only-decl"
                    "unused-template"
                    (metric "frag61-template-body" "Template body (should NOT appear)" 999.0)
                metric "frag61-decl-only-metric-after" "Also renders normally" 200.0 ] ]

// Card 2 — decl + two refs. The body's interior ids ("title", "value")
// get namespaced under each ref's id, so the rendered DOM carries
// `data-fuaran-node-id="frag61-ref-a.title"` etc.

let private fragmentBody: Node<Msg> =
    card
        "frag61-body-card"
        "Reusable card template"
        [ heading "title" 3 "Template title"; metric "value" "Template value" 42.0 ]

let private singleRefCard: Node<Msg> =
    card
        "frag61-single-ref-card"
        "Two refs to the same fragment — interior ids namespaced"
        [ paragraph
              "frag61-single-ref-body"
              "Two FragmentRefs expand the same template body. The renderer namespaces each interior NodeId under the ref's id so the DOM stays addressable per-instance. Inspect with devtools: each Metric carries a distinct data-fuaran-node-id (frag61-ref-a.value vs frag61-ref-b.value)."
          dashboard
              "frag61-single-ref-dash"
              [ fragmentDecl "frag61-card-template-decl" "frag61-card-template" fragmentBody
                Fuaran.fragmentRef "frag61-ref-a" "frag61-card-template"
                Fuaran.fragmentRef "frag61-ref-b" "frag61-card-template" ] ]

// Card 3 — cycle. Decl A's body references B; decl B's body references A.
// The runtime guard catches the cycle and renders a placeholder. (The
// validator's FUARAN058 catches this at build time too; this card is
// the runtime safety-net demonstration.)
//
// Implementation note: the cycle has to be expressed via two decls
// whose bodies reference each other — the fragmentBody type is just
// `Node<Msg>`, not `unit lazy_`, so a literal self-reference inside one
// decl's Body isn't expressible (the F# typechecker rejects forward
// references to `cycleA` from inside its own Body expression). The
// two-decl shape produces the same render-time loop.

let private cycleDeclA: Node<Msg> =
    fragmentDecl
        "frag61-cycle-decl-a"
        "cycle-a"
        (card
            "frag61-cycle-a-body"
            "cycle-a body"
            [ paragraph "frag61-cycle-a-text" "I reference cycle-b inside my body."
              Fuaran.fragmentRef "frag61-cycle-a-ref-b" "cycle-b" ])

let private cycleDeclB: Node<Msg> =
    fragmentDecl
        "frag61-cycle-decl-b"
        "cycle-b"
        (card
            "frag61-cycle-b-body"
            "cycle-b body"
            [ paragraph "frag61-cycle-b-text" "And I reference cycle-a inside MY body — the cycle."
              Fuaran.fragmentRef "frag61-cycle-b-ref-a" "cycle-a" ])

let private cycleCard: Node<Msg> =
    card
        "frag61-cycle-card"
        "Cycle guard — runtime renders a labelled placeholder"
        [ paragraph
              "frag61-cycle-body"
              "Two decls reference each other; expanding either triggers an infinite loop. The renderer's cycle-guard substitutes a labelled placeholder on the second expansion of the same fragment name. Inspect the captured-warnings panel below for the runtime's diagnostic emission. (FUARAN058 catches this at build time too — this card demonstrates the safety net.)"
          dashboard "frag61-cycle-dash" [ cycleDeclA; cycleDeclB; Fuaran.fragmentRef "frag61-cycle-ref" "cycle-a" ] ]

let private rootDashboard: Node<Msg> =
    dashboard
        "frag61-root"
        [ heading "frag61-title" 1 "Reusable subtree fragments"
          declOnlyCard
          singleRefCard
          cycleCard ]

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
          Fragments = Render.collectFragments Map.empty rootDashboard
          ExpandingFragments = Set.empty
          Scope = None }

    React.Fragment
        [ Render.themeStyleElement Defaults.theme
          Html.div
              [ prop.id "fragments-61-page"
                prop.style [ style.padding 24 ]
                prop.children
                    [ Render.render ctx rootDashboard
                      Html.section
                          [ prop.custom ("data-testid", "frag61-captured-warnings")
                            prop.style [ style.marginTop 32; style.fontFamily "monospace" ]
                            prop.children
                                [ Html.h2
                                      [ prop.text "Captured runtime warnings (fragment unresolved/cycle diagnostics)" ]
                                  if model.Warnings.IsEmpty then
                                      Html.p [ prop.text "(none yet)" ]
                                  else
                                      Html.ol
                                          [ prop.children
                                                [ for w in List.rev model.Warnings -> Html.li [ prop.text w ] ] ] ] ] ] ] ]
