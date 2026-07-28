module Fuaran.Samples.Catalog.InteractiveDisabled130

// ============================================================================
//  Catalog page for Phase 130 — bound disabled-state across interactive kinds.
//
//  The interactive-state class-fix (Phase 130) generalised Button's bindable
//  `Disabled` slot (Phase 129) to the rest of the node-addressable interactive
//  surface: Select, Form, and FileUpload each gained a `Disabled :
//  Binding<bool> option` slot the renderer honours (emitting the HTML
//  `disabled` attribute / a disabled `<fieldset>` wrapper for the form).
//
//  Shape: one reader pane renders a Button + Select + Form + FileUpload, each
//  binding its `Disabled` slot to the SAME global `Binding.State "busy"` key. A
//  toggle pane writes that key through `Action.setState`. Click "Start" and
//  EVERY interactive control disables at once; click "Finish" and they all
//  re-enable — the single-state-key-drives-the-whole-class demonstration the
//  phase exists to make possible. The reader pane uses `renderStateReactive`
//  so the StateStore subscription re-renders it on each toggle (the Elmish
//  model is `unit`).
//
//  Mounted at `?interactive-disabled-130=1`. Browser counterpart to the .NET
//  `Phase 130 interactive-state coverage` property test (which pins the
//  ReplaceBinding / getBindingValue dispatch tables for the same slots).
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

// ─── The shared global key every interactive control's Disabled slot reads ───

let private busyKey = "busy"

/// The bound disabled-state shared by every interactive control on the page.
let private busyBinding: Binding<bool> = binding.state busyKey false

// Each interactive kind binds its `Disabled` slot to `busyBinding`, so a single
// SetState on `busyKey` disables / enables the whole class at once.
let private readerPane: Node<Msg> =
    Fuaran.stack
        "interactive-disabled-pane"
        { Defaults.stack with
            Children =
                [ Fuaran.heading
                      "idp-title"
                      { Defaults.heading with
                          Level = 3
                          Text = TextSource.Literal "Bound disabled across interactive kinds" }
                  Fuaran.button
                      "idp-button"
                      { Defaults.button<Msg> with
                          Label = TextSource.Literal "Calculate"
                          Variant = ButtonVariant.Primary
                          Disabled = Some busyBinding }
                  Fuaran.select
                      "idp-select"
                      { Defaults.select<Msg> with
                          Label = TextSource.Literal "Region"
                          Source =
                              Binding.Static(Some [ { Value = "uk"; Label = "UK" }; { Value = "us"; Label = "US" } ])
                          Value = Binding.Static(Some(Some "uk"))
                          Disabled = Some busyBinding }
                  Fuaran.form
                      "idp-form"
                      { Defaults.form<Msg> with
                          SubmitLabel = TextSource.Literal "Submit"
                          Disabled = Some busyBinding
                          Fields =
                              [ { Defaults.formField<Msg> with
                                    Id = "idp-form-name"
                                    Label = TextSource.Literal "Name"
                                    Kind =
                                        FormFieldKind.Text(
                                            Some(Binding.Static(Some "")),
                                            Some(fun _ -> Action.Chain [])
                                        ) } ] }
                  Fuaran.fileUpload
                      "idp-upload"
                      { Defaults.fileUpload<Msg> with
                          Label = TextSource.Literal "Upload CSV"
                          Accept = [ ".csv" ]
                          Disabled = Some busyBinding } ] }

// The toggle: two buttons writing the global `busy` key through the
// BrowserRuntime so `Action.setState` actually reaches `StateStore.set`.
let private togglePane: Node<Msg> =
    Fuaran.stack
        "busy-toggle"
        { Defaults.stack with
            Orientation = Orientation.Horizontal
            Children =
                [ Fuaran.button
                      "set-busy"
                      { Defaults.button<Msg> with
                          Label = TextSource.Literal "Start (disable all)"
                          Variant = ButtonVariant.Secondary
                          OnClick = Action.setState busyKey (JBool true) }
                  Fuaran.button
                      "clear-busy"
                      { Defaults.button<Msg> with
                          Label = TextSource.Literal "Finish (enable all)"
                          Variant = ButtonVariant.Primary
                          OnClick = Action.setState busyKey (JBool false) } ] }

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
          SessionContext = Map.empty }

    React.Fragment
        [ Render.themeStyleElement Defaults.theme
          Html.div
              [ prop.id "interactive-disabled-130-page"
                prop.className "catalog-interactive-disabled"
                prop.style [ style.padding 24; style.maxWidth 880 ]
                prop.children
                    [ Html.h1 [ prop.text "Bound disabled-state across interactive kinds (Phase 130)" ]
                      Html.p
                          [ prop.text
                                "Button, Select, Form, and FileUpload each bind their Disabled slot to the same global Binding.State \"busy\" key. Click Start and every control disables at once; click Finish and they all re-enable — one state key drives the whole interactive class." ]
                      Html.section
                          [ prop.style [ style.marginTop 16 ]
                            prop.children [ Render.render toggleCtx togglePane ] ]
                      Html.section
                          [ prop.testId "interactive-disabled-readers"
                            prop.style [ style.marginTop 24; style.maxWidth 480 ]
                            prop.children [ Render.renderStateReactive BindingResolver.empty dispatch readerPane ] ] ] ] ]
