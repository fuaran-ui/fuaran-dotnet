module FuaranStarter.Main

// The 30-second F# on-ramp: author a typed Fuaran tree, render it through the
// language-tier renderer, run the Elmish dispatch loop. Everything an AI
// orchestrator can emit, you can render here.
//
// The shape is MVU (Model-View-Update), the same loop Elmish drives everywhere:
//   Model --tree--> a typed Node tree --Render.render--> DOM
//        <--update-- a Msg <--dispatch-- user interaction

open Fable.Core.JsInterop
open Elmish
open Elmish.React
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Renderer

importSideEffects "./index.css"

// ─── Model + Msg (the MVU core) ─────────────────────────────────────────────

type Model = { Counter: int }

type Msg =
    | Increment
    | Decrement

let init () : Model * Cmd<Msg> = { Counter = 0 }, Cmd.none

let update (msg: Msg) (model: Model) : Model * Cmd<Msg> =
    match msg with
    | Increment ->
        { model with
            Counter = model.Counter + 1 },
        Cmd.none
    | Decrement ->
        { model with
            Counter = model.Counter - 1 },
        Cmd.none

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
                          Source = binding.state "counter" 0.0 }

                  Fuaran.stack
                      "counter-buttons"
                      { Defaults.stack with
                          Orientation = Horizontal
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

// ─── View (Elmish shape: Model -> dispatch -> ReactElement) ─────────────────
//
// `Render.renderWithSources` is the minimal render entry point — it supplies a
// default diagnostic runtime + the no-op visualisation adapter, so a starter
// needs no host-runtime wiring. Reach for the full `Render.render ctx tree`
// (with a `RenderContext`) once you wire a real `IFuaranRuntime`, a
// visualisation adapter (e.g. for Grid/Chart nodes), or a telemetry sink.

let view (model: Model) (dispatch: Msg -> unit) =
    Render.renderWithSources (buildSources model) dispatch (tree model)

// ─── Boot the Elmish program ────────────────────────────────────────────────

Program.mkProgram init update view
|> Program.withReactSynchronous "fuaran-app-root"
|> Program.run
