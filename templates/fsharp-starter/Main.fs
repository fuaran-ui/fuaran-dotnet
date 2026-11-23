module FuaranStarter.Main

// The 30-second F# on-ramp: author a typed Fuaran tree, render it through the
// language-tier renderer, run it on the Fuaran.MVU runtime. Everything an AI
// orchestrator can emit, you can render here.
//
// The shape is MVU (Model-View-Update):
//   Model --tree--> a typed Node tree --Render.render--> DOM
//        <--update-- a Msg <--dispatch-- user interaction

open Fable.Core.JsInterop
open Fuaran.MVU
open Fuaran.MVU.React
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Renderer

importSideEffects "./index.css"

// ─── Model + Msg (the MVU core) ─────────────────────────────────────────────
//
// `update` is pure: it returns the next model and the steps it asks for. A step
// is an effect to perform (`Do`) or a follow-up message (`Then`). This starter
// performs nothing, so its effect type is `NoEffect` and every step list is [].

type Model = { Counter: int }

type Msg =
    | Increment
    | Decrement

let init () : Model * Step<Msg, NoEffect> list = { Counter = 0 }, []

let update (msg: Msg) (model: Model) : Model * Step<Msg, NoEffect> list =
    match msg with
    | Increment ->
        { model with
            Counter = model.Counter + 1 },
        []
    | Decrement ->
        { model with
            Counter = model.Counter - 1 },
        []

// ─── The authored tree (a pure projection of Model) ─────────────────────────
//
// This is the one function you grow as your app grows: add `Fuaran.*` nodes
// here, add the state they read in `buildSources`, add the messages they emit
// in `Msg`. Controlled values use `binding.state key default`, resolved against
// `buildSources` below.

let private tree (_model: Model) : Node<Msg> =
    Fuaran.dashboard
        "app-root"
        { Defaults.dashboard with
            Children =
                [ Fuaran.heading
                      "title"
                      { Defaults.heading with
                          Level = 1
                          Text = TextSource.Literal "Hello, Fuaran" }

                  Fuaran.metric
                      "counter-kpi"
                      { Defaults.metric with
                          Label = TextSource.Literal "Counter"
                          Value = binding.state "counter" 0.0 }

                  Fuaran.stack
                      "counter-buttons"
                      { Defaults.stack with
                          Orientation = Orientation.Horizontal
                          Children =
                              [ Fuaran.button
                                    "btn-decrement"
                                    { Defaults.button with
                                        Label = TextSource.Literal "− Decrement"
                                        OnClick = Action.dispatch Decrement
                                        Variant = ButtonVariant.Secondary }

                                Fuaran.button
                                    "btn-increment"
                                    { Defaults.button with
                                        Label = TextSource.Literal "+ Increment"
                                        OnClick = Action.dispatch Increment
                                        Variant = ButtonVariant.Primary } ] } ] }

// ─── BindingSources (the data side of the tree) ─────────────────────────────
//
// Every `binding.state key …` in the tree reads `State.[key]` here. Rebuilt
// each render from `Model`. Add a `QueryResults` entry for non-controlled data
// (`binding.query key accessor`).

let private buildSources (model: Model) : BindingResolver.BindingSources =
    { BindingResolver.empty with
        State = Map.ofList [ "counter", box (float model.Counter) ] }

// ─── View (Model -> dispatch -> ReactElement) ──────────────────────────────
//
// `Render.renderWithSources` is the minimal render entry point — it supplies a
// default diagnostic runtime + the no-op visualisation adapter, so a starter
// needs no host-runtime wiring. Reach for the full `Render.render ctx tree`
// (with a `RenderContext`) once you wire a real `IFuaranRuntime`, a
// visualisation adapter (e.g. for Grid/Chart nodes), or a telemetry sink.

let view (model: Model) (dispatch: Msg -> unit) =
    Render.renderWithSources (buildSources model) dispatch (tree model)

// ─── Mount the component ────────────────────────────────────────────────────
//
// The component is the four pure functions; `ElementView.ofView` runs a view
// that returns a React element on the runtime. The interpreter performs the
// component's effects and starts its resources: this one has neither. When the
// app needs the browser (clipboard, navigation, storage, timers, file reads),
// make `Fuaran.MVU.Browser.StandardEffect` a case of your effect type and use
// `StandardInterpreter.embed`; `Live.host` is the browser host.

let private app: Component<unit, Model, Msg, NoEffect, NoResource, ElementView<Msg>> =
    Component.plain init update (fun _ -> Map.empty) (ElementView.ofView view)

let private interpreter: Interpreter<Msg, NoEffect, NoResource> =
    Interpreter.create [] (fun NoEffect -> failwith "no effects") (fun _ NoResource -> failwith "no resources")

Live.elements "fuaran-app-root" Fresh (Fuaran.MVU.Browser.Live.host ignore) app interpreter ()
|> ignore
