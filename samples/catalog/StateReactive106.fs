module Fuaran.Samples.Catalog.StateReactive106

// ============================================================================
//  Catalog page for Phase 106 — state-channel-driven live re-render of all
//  `Binding.State` readers.
//
//  Shape: TWO independent reader panes, each its own `Render.renderStateReactive`
//  surface, both reading the SAME global `Binding.State` key ("terms"). A
//  third pane carries two toggle buttons whose `OnClick = Action.setState
//  "terms" …` writes the key through the `BrowserRuntime` → `StateStore.set`
//  path. Because each reader pane opts into `renderStateReactive`, a single
//  global `SetState` re-renders BOTH panes at once — not just the control that
//  owns the value. The Elmish model is `unit`: the whole point is that the
//  surfaces refresh from the StateStore subscription, NOT from an Elmish tick.
//
//  Mounted at `?state-reactive-106=1`. This is the browser counterpart to the
//  `.NET` `StateReactiveTests` (which pin `collectStateKeys` + the
//  `StateStore.subscribeKeys` re-render trigger directly).
// ============================================================================

open Feliz
open Fuaran.UI
open Fuaran.Core
open Fuaran.UI.Types
open Fuaran.UI.Renderer

// ─── Model (state lives in the StateStore, not here) ────────────────────────

type Model = unit
type Msg = NoOp

let init () : Model = ()
let update (_: Msg) (model: Model) : Model = model

// F# 10 `box _` types as `obj | null`.
let private nn (value: 'T) : obj = box value |> Unchecked.nonNull

// ─── The shared global key ──────────────────────────────────────────────────

let private termsKey = "terms"
let private termsDefault = "cash"

// Each reader pane is an independent Fuaran tree; both bind the same key. The
// renderer auto-subscribes whichever surface reads `Binding.State termsKey`.
let private readerPane (paneId: string) (heading: string) : Node<Msg> =
    Fuaran.stack
        paneId
        { Defaults.stack with
            Children =
                [ Fuaran.heading
                      (paneId + "-title")
                      { Defaults.heading with
                          Level = 3
                          Text = TextSource.Literal heading }
                  Fuaran.labelValueRow
                      (paneId + "-row")
                      { Defaults.labelValueRow with
                          Label = TextSource.Literal "Active terms mode"
                          // A Binding.State-bound TextSource isn't a numeric
                          // source, so surface the live value via a heading
                          // below; the row keeps the visual rhythm.
                          Value = binding.``static`` 0.0 }
                  Fuaran.heading
                      (paneId + "-value")
                      { Defaults.heading with
                          Level = 2
                          Variant = HeadingVariant.Lead
                          // The live reader: resolves to the StateStore value
                          // for `termsKey`, falling back to the default.
                          Text = TextSource.Bound(binding.state termsKey termsDefault) } ] }

// The toggle: two buttons writing the global key. Rendered through the
// BrowserRuntime so `Action.setState` actually reaches `StateStore.set`.
let private togglePane: Node<Msg> =
    Fuaran.stack
        "terms-toggle"
        { Defaults.stack with
            Orientation = Orientation.Horizontal
            Children =
                [ Fuaran.button
                      "set-cash"
                      { Defaults.button<Msg> with
                          Label = TextSource.Literal "Cash terms"
                          Variant = ButtonVariant.Secondary
                          OnClick = Action.setState termsKey (JStr "cash") }
                  Fuaran.button
                      "set-real"
                      { Defaults.button<Msg> with
                          Label = TextSource.Literal "Real terms"
                          Variant = ButtonVariant.Primary
                          OnClick = Action.setState termsKey (JStr "real") } ] }

let view (_model: Model) (dispatch: Msg -> unit) : ReactElement =
    // Toggle context: a real BrowserRuntime so SetState writes the store.
    let toggleCtx: Render.RenderContext<Msg> =
        { Sources = BindingResolver.empty
          Runtime = BrowserRuntime.create ()
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
          EgressPolicy = Sanitize.permissiveEgress }

    React.Fragment
        [ Render.themeStyleElement Defaults.theme
          Html.div
              [ prop.id "state-reactive-106-page"
                prop.className "catalog-state-reactive"
                prop.style [ style.padding 24; style.maxWidth 880 ]
                prop.children
                    [ Html.h1 [ prop.text "State-channel live re-render (Phase 106)" ]
                      Html.p
                          [ prop.text
                                "Two independent panes each read the same global Binding.State key via renderStateReactive. Click a toggle: a single global Action.SetState re-renders BOTH panes at once — not just the control that owns the value. The Elmish model is unit; the refresh comes from the StateStore subscription." ]
                      Html.section
                          [ prop.style [ style.marginTop 16 ]
                            prop.children [ Render.render toggleCtx togglePane ] ]
                      Html.section
                          [ prop.style [ style.marginTop 24; style.display.flex; style.gap 24 ]
                            prop.children
                                [ Html.div
                                      [ prop.testId "reader-pane-a"
                                        prop.style [ style.flexGrow 1; style.padding 12 ]
                                        prop.children
                                            [ Render.renderStateReactive
                                                  BindingResolver.empty
                                                  dispatch
                                                  (readerPane "pane-a" "Pane A") ] ]
                                  Html.div
                                      [ prop.testId "reader-pane-b"
                                        prop.style [ style.flexGrow 1; style.padding 12 ]
                                        prop.children
                                            [ Render.renderStateReactive
                                                  BindingResolver.empty
                                                  dispatch
                                                  (readerPane "pane-b" "Pane B") ] ] ] ] ] ] ]
