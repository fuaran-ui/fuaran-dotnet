// Fuaran.UI.Cli — the F#/Fable integration scaffold.
//
// The dotnet tool emits the F# (`fsharp-fable`) target natively; the canonical
// multi-target scaffolder (ts-react + fsharp-fable, plus the browser-BYOK
// variant) is the single-sourced `@fuaran-ui/cli` / MCP `scaffold` tool. The
// F#/Fable leg is always server-proxied — pair it with a proxy route that injects
// the endpoint + secrets from server-side env.

module Fuaran.UI.Cli.Scaffold

/// The emitted F#/Fable panel: a turn-loop over the Fuaran generation endpoint
/// rendered through Fuaran.UI.Renderer. Server-proxied — no secret in the client.
let fsharpFablePanel: string =
    """// FuaranPanel.fs — a prompt->UI panel over the Fuaran generation endpoint.
// Server-proxied pattern: this calls YOUR same-origin proxy route (e.g.
// "/api/fuaran"), which injects the access token + BYOK key from server-side
// env and forwards to the endpoint — no secret ever reaches the client bundle.
module App.FuaranPanel

open Elmish
open Feliz
open Fuaran.UI.Client
open Fuaran.UI.Renderer

type Model =
    { Prompt: string
      TreeJson: string option
      Status: string }

type Msg =
    | SetPrompt of string
    | Submit
    | Produced of string
    | Failed of string

// One client + session for the app. Point Endpoint at your same-origin proxy.
let private session =
    FuaranSession(FuaranClient(FuaranClientConfig.create "/api/fuaran"))

let init () = { Prompt = ""; TreeJson = None; Status = "" }, Cmd.none

let update msg model =
    match msg with
    | SetPrompt p -> { model with Prompt = p }, Cmd.none
    | Submit ->
        let run () =
            async {
                match! session.Next model.Prompt with
                | TurnResult.Produced(treeJson, _, _) -> return Produced treeJson
                | TurnResult.AccessDenied reason -> return Failed $"access denied: {reason}"
                | TurnResult.TurnFailed err -> return Failed $"turn failed [{TurnStage.label err.Stage}]: {err.Message}"
            }

        model, Cmd.OfAsync.result (run ())
    | Produced treeJson -> { model with TreeJson = Some treeJson; Status = "" }, Cmd.none
    | Failed message -> { model with Status = message }, Cmd.none

let view model dispatch =
    Html.div
        [ Html.input [ prop.value model.Prompt; prop.onChange (SetPrompt >> dispatch) ]
          Html.button [ prop.text "Generate"; prop.onClick (fun _ -> dispatch Submit) ]
          (if model.Status <> "" then
               Html.p [ prop.role "status"; prop.text model.Status ]
           else
               Html.none)
          // Decode the produced tree and render it through Fuaran.UI.Renderer.
          match model.TreeJson with
          | Some treeJson ->
              match Render.decodeTreeJson treeJson with
              | Ok tree -> Render.renderWithSources BindingResolver.BindingSources.empty (fun _ -> ()) tree
              | Error e -> Html.p [ prop.text $"decode failed at {e.Path}: {e.Message}" ]
          | None -> Html.none ]
"""

/// The reference server-side proxy note the F#/Fable leg pairs with.
let proxyNote: string =
    "The F#/Fable leg is always server-proxied: pair it with a proxy route that injects "
    + "FUARAN_ENDPOINT / FUARAN_ACCESS_TOKEN / FUARAN_PROVIDER_KEY from server-side env, so no "
    + "secret reaches the client bundle. The ts-react scaffold (@fuaran-ui/cli scaffold --target ts) "
    + "emits a reference proxy you can port."
