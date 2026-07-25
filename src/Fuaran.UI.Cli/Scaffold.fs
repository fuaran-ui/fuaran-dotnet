// Fuaran.UI.Cli — the F#/Fable integration scaffold.
//
// The dotnet tool emits the F# (`fsharp-fable`) target natively; the canonical
// multi-target scaffolder (ts-react + fsharp-fable, plus the browser-BYOK
// variant) is the single-sourced `@fuaran-ui/cli` / MCP `scaffold` tool. The
// F#/Fable leg is always server-proxied — pair it with a proxy route that injects
// the endpoint + secrets from server-side env.
//
// PARITY (load-bearing): `fuaranFablePanel` below is kept byte-identical to the
// canonical `PANEL_FSHARP` template in the TS scaffolder, so `fuaran scaffold
// --target fsharp` and `fuaran_scaffold target=fsharp-fable` emit the same file.
// The emitted panel is a FABLE client, so it must NOT reference `Fuaran.UI.Client`
// (that package is plain .NET — it opens System.Net.Http and is not source-packed
// for Fable). The browser leg speaks the wire contract directly: `fetch` to the
// proxy, `JsonDecode.decodeNodeObj`, then `Render.renderWithSources` over
// `BindingResolver.empty`. `Fuaran.UI.Client` belongs on the SERVER side of the
// proxy — see `samples/sdk-integration/`.

module Fuaran.UI.Cli.Scaffold

/// The emitted F#/Fable panel: a prompt→UI turn-loop over the proxy route,
/// decoded with the canonical codec and rendered through Fuaran.UI.Renderer.
/// Server-proxied — no secret in the client bundle.
let fsharpFablePanel: string =
    """module App.FuaranPanel

// A prompt→UI panel over the Fuaran generation endpoint, for a Fable + Feliz
// app. Server-proxied pattern: posts to YOUR same-origin proxy route (which
// injects the access token + BYOK key from server-side env — see the ts-react
// scaffold's server/fuaranProxy.ts for a reference proxy), decodes the
// produced wire tree with the canonical decoder, and renders it with the
// reference renderer.
//
// NuGet: Fuaran.UI · Fuaran.UI.Ops · Fuaran.UI.Renderer · Fable.Fetch · Feliz
// Import the reference stylesheet once in your app entry.

open Fable.Core
open Fable.Core.JsInterop
open Fetch
open Feliz
open Fuaran.UI
open Fuaran.UI.Ops
open Fuaran.UI.Renderer

/// POST the prompt (+ the previous turn's tree, making the turn a cheap
/// repair diff) to the proxy; return the produced tree JSON or an error.
let private generate (prompt: string) (currentTreeJson: string option) : JS.Promise<Result<string, string>> =
    promise {
        let body =
            createObj
                [ "Prompt" ==> prompt
                  match currentTreeJson with
                  | Some t -> "CurrentTreeJson" ==> t
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
            return Ok(parsed?TreeJson |> string)
        else
            return Error(sprintf "generation failed (HTTP %d): %s" response.Status text)
    }

[<ReactComponent>]
let FuaranPanel () =
    let prompt, setPrompt = React.useState ""
    let treeJson, setTreeJson = React.useState<string option> None
    let status, setStatus = React.useState ""
    let busy, setBusy = React.useState false

    let run () =
        if prompt.Trim() <> "" && not busy then
            setBusy true
            setStatus "generating…"

            generate prompt treeJson
            |> Promise.iter (fun result ->
                (match result with
                 | Ok json ->
                     setTreeJson (Some json)
                     setStatus ""
                 | Error message -> setStatus message)

                setBusy false)

    let rendered =
        treeJson
        |> Option.map (fun json ->
            // Decode through the canonical codec — the same decoder every
            // conformant host trusts. Dispatch is a no-op until you wire the
            // panel's actions into your app's update loop.
            match JsonDecode.decodeNodeObj json with
            | Ok tree -> Render.renderWithSources BindingResolver.empty ignore tree
            | Error e -> Html.p [ prop.role "status"; prop.text (sprintf "decode failed at %s: %s" e.Path e.Message) ])

    Html.section
        [ prop.children
              [ Html.form
                    [ prop.onSubmit (fun e ->
                          e.preventDefault ()
                          run ())
                      prop.children
                          [ Html.input
                                [ prop.value prompt
                                  prop.onChange setPrompt
                                  prop.placeholder "Describe the UI you want…"
                                  prop.ariaLabel "Fuaran prompt" ]
                            Html.button [ prop.type' "submit"; prop.disabled busy; prop.text "Generate" ] ]
                      ]
                if status <> "" then
                    Html.p [ prop.role "status"; prop.text status ]
                match rendered with
                | Some el -> el
                | None -> Html.none ] ]
"""

/// The reference server-side proxy note the F#/Fable leg pairs with.
let proxyNote: string =
    "The F#/Fable leg is always server-proxied: pair it with a proxy route that injects "
    + "FUARAN_ENDPOINT / FUARAN_ACCESS_TOKEN / FUARAN_PROVIDER_KEY from server-side env, so no "
    + "secret reaches the client bundle. A worked .NET proxy (using Fuaran.UI.Client on the "
    + "server) ships at samples/sdk-integration/; the ts-react scaffold emits a Node equivalent."
