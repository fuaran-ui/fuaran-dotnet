// The Fable/Elmish edge: the HTTP effect, the Elmish wiring, and the view.
//
// Everything decision-shaped lives in the portable `Turn` module; this file only
// supplies what the browser can do — POST to the proxy, and render. Note it does
// NOT reference `Fuaran.UI.Client`: that package is plain .NET (System.Net.Http)
// and is not source-packed for Fable. The browser speaks the wire contract
// directly, and the .NET client sits on the SERVER side of the proxy
// (../server/), which is where the credentials live.

module Fuaran.Sample.SdkIntegration.Client.App

open Fable.Core
open Fable.Core.JsInterop
open Fetch
open Elmish
open Elmish.React
open Feliz

open Fuaran.UI.Ops
open Fuaran.UI.Renderer

open Fuaran.Sample.SdkIntegration.Client.Turn

/// POST the turn to the same-origin proxy route. The proxy adds the access
/// token + BYOK key server-side, so nothing secret is present here.
let private runTurn (request: TurnRequest) : JS.Promise<Msg> =
    promise {
        let body =
            createObj
                [ "Prompt" ==> request.Prompt
                  match request.CurrentTreeJson with
                  | Some tree -> "CurrentTreeJson" ==> tree
                  | None -> () ]

        let! response =
            fetch
                "/api/fuaran"
                [ Method HttpMethod.POST
                  requestHeaders [ HttpRequestHeaders.ContentType "application/json" ]
                  Body !^(JS.JSON.stringify body) ]

        let! text = response.text ()

        if response.Ok then
            let parsed = JS.JSON.parse text
            return Produced(parsed?TreeJson |> string)
        else
            return TurnFailed(sprintf "generation failed (HTTP %d): %s" response.Status text)
    }

/// The Elmish `update`: delegate the decision to the portable core, then run
/// whatever effect it asked for.
let update (msg: Msg) (model: Model) : Model * Cmd<Msg> =
    let next, request = Turn.update msg model

    match request with
    | None -> next, Cmd.none
    | Some turn -> next, Cmd.OfPromise.either runTurn turn id (fun ex -> TurnFailed ex.Message)

let init () : Model * Cmd<Msg> = Turn.init (), Cmd.none

/// Decode the held tree with the canonical codec and render it through the
/// reference renderer. Dispatch is a no-op until the panel's actions are wired
/// into this app's own update loop.
let private renderTree (treeJson: string) =
    match JsonDecode.decodeNodeObj treeJson with
    | Ok tree -> Render.renderWithSources BindingResolver.empty ignore tree
    | Error e ->
        Html.p
            [ prop.role "alert"
              prop.text (sprintf "decode failed at %s: %s" e.Path e.Message) ]

let view (model: Model) (dispatch: Msg -> unit) =
    Html.section
        [ prop.children
              [ Html.form
                    [ prop.onSubmit (fun e ->
                          e.preventDefault ()
                          dispatch Submit)
                      prop.children
                          [ Html.input
                                [ prop.value model.Prompt
                                  prop.onChange (SetPrompt >> dispatch)
                                  prop.placeholder "Describe the UI you want…"
                                  prop.ariaLabel "Fuaran prompt" ]
                            Html.button
                                [ prop.type' "submit"
                                  prop.disabled (model.Status = Status.Generating)
                                  prop.text "Generate" ] ] ]

                match model.Status with
                | Status.Generating -> Html.p [ prop.role "status"; prop.text "Generating…" ]
                | Status.Error message -> Html.p [ prop.role "alert"; prop.text message ]
                | Status.Idle
                | Status.Ready -> Html.none

                // The held tree keeps rendering across a failed turn, so the last
                // good UI stays on screen.
                match model.TreeJson with
                | Some treeJson -> renderTree treeJson
                | None -> Html.none ] ]

Program.mkProgram init update view
|> Program.withReactSynchronous "app"
|> Program.run
