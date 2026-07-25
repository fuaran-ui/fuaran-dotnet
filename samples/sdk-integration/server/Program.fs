// The ASP.NET host for the server-proxied BYOK sample.
//
// Two routes:
//   POST /api/fuaran  — the proxy: the browser sends a prompt, the server adds
//                       the credentials and runs the turn (see Proxy.fs).
//   GET  /            — a tiny page so the sample is runnable on its own; a real
//                       app serves its Fable/React bundle here instead.
//
// Configuration (environment only — never the repo):
//   FUARAN_ENDPOINT       the generation endpoint URL
//   FUARAN_ACCESS_TOKEN   the paid access token
//   FUARAN_PROVIDER_KEY   the BYOK provider key
//
// With none of them set the sample points at the local mock
// (`npx @fuaran-ui/mock`), so it runs end to end offline with no token and no
// BYOK spend.

module Fuaran.Sample.SdkIntegration.Server.Program

open System
open System.IO
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http

open Fuaran.UI.Client

[<Literal>]
let private MockEndpoint = "http://127.0.0.1:8123"

let private envOpt (name: string) =
    match Environment.GetEnvironmentVariable name with
    | null
    | "" -> None
    | v -> Some v

/// Build the server-side client from environment configuration, falling back to
/// the local mock so the sample is runnable with nothing configured.
let buildClient () : FuaranClient * string =
    match envOpt "FUARAN_ENDPOINT" with
    | Some endpoint ->
        FuaranClient(
            { FuaranClientConfig.create endpoint with
                AccessToken = envOpt "FUARAN_ACCESS_TOKEN"
                ProviderKey = envOpt "FUARAN_PROVIDER_KEY" }
        ),
        endpoint
    | None -> FuaranClient(FuaranClientConfig.create MockEndpoint), MockEndpoint + " (local mock)"

let private page =
    """<!doctype html>
<html lang="en">
  <head>
    <meta charset="utf-8" />
    <title>Fuaran SDK integration sample</title>
    <link rel="stylesheet" href="/fuaran-reference.css" />
  </head>
  <body>
    <main id="app">
      <h1>Fuaran SDK integration sample</h1>
      <p>
        The server-proxied BYOK pattern. POST a prompt to
        <code>/api/fuaran</code> &mdash; the server adds the access token and
        BYOK key, so no secret reaches this page.
      </p>
      <p>
        Mount the Fable/Elmish client (see <code>../client/</code>) into this
        element to drive the loop from the browser.
      </p>
    </main>
  </body>
</html>"""

[<EntryPoint>]
let main argv =
    let client, describedEndpoint = buildClient ()

    let builder =
        WebApplication.CreateBuilder(
            WebApplicationOptions(Args = argv, WebRootPath = Path.Combine(AppContext.BaseDirectory, "wwwroot"))
        )

    let app = builder.Build()
    app.UseStaticFiles() |> ignore

    app.MapGet(
        "/",
        RequestDelegate(fun ctx ->
            ctx.Response.ContentType <- "text/html; charset=utf-8"
            ctx.Response.WriteAsync page)
    )
    |> ignore

    app.MapPost(
        "/api/fuaran",
        RequestDelegate(fun ctx ->
            task {
                use reader = new StreamReader(ctx.Request.Body)
                let! body = reader.ReadToEndAsync()
                let! status, payload = Proxy.handle client body |> Async.StartAsTask
                ctx.Response.StatusCode <- status
                ctx.Response.ContentType <- "application/json"
                do! ctx.Response.WriteAsync payload
            }
            :> System.Threading.Tasks.Task)
    )
    |> ignore

    printfn "Fuaran SDK integration sample — proxying to %s" describedEndpoint
    printfn "Listening on http://localhost:14040"
    app.Run "http://localhost:14040"
    0
