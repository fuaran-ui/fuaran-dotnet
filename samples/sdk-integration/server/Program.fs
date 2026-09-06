// The ASP.NET host for the server-proxied BYOK sample.
//
// Two routes:
//   POST /api/fuaran  — the proxy: the browser sends a prompt, the server adds
//                       the credentials and runs the turn (see Proxy.fs).
//   GET  /            — a tiny page so the sample is runnable on its own; a real
//                       app serves its Fable/React bundle here instead.
//
// Configuration (environment only — never the repo):
//   FUARAN_ENDPOINT           the generation endpoint URL
//   FUARAN_ACCESS_TOKEN       the paid access token
//   FUARAN_PROVIDER_KEY       the BYOK provider key
//   FUARAN_SAMPLE_SECRET      the shared secret callers present as the
//                             `X-Sample-Secret` header on /api/fuaran
//   FUARAN_SAMPLE_ANONYMOUS=1 serve every caller instead — a deliberate opt-out
//
// With none of the first three set, the sample points at the local mock
// (`npx @fuaran-ui/mock`), so it runs end to end offline with no token and no
// BYOK spend.
//
// THE ROUTE IS FAIL-CLOSED ON AUTHORISATION. This process holds a BYOK key and
// answers whoever can reach it, so it refuses to start without either a shared
// secret or an explicit anonymous opt-out, and the startup lines say which arm
// is live. `Proxy.fs` carries the prompt cap and the per-caller rate limit;
// both are on by default and neither is settable from the wire.

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

/// The header a caller presents the shared secret in. A header rather than a
/// body member for the reason the endpoint itself gives: a body is the thing
/// most likely to be logged wholesale.
[<Literal>]
let SecretHeader = "X-Sample-Secret"

/// Resolve the route's authorisation arm, or refuse to start. There is no third
/// answer on purpose: a sample that quietly served everyone when a variable was
/// unset would be copied into something that quietly serves everyone.
///
/// Returns a function from the secret a request presented to the policy for
/// that request. The RATE LIMITER is built once and shared by every policy the
/// function returns — a per-request limiter would count to one, forever.
let buildPolicy () : Result<(string option -> Proxy.ProxyPolicy) * string, string> =
    let baseline = Proxy.Policy.create Proxy.Policy.anonymous

    match envOpt "FUARAN_SAMPLE_SECRET", envOpt "FUARAN_SAMPLE_ANONYMOUS" with
    | Some secret, _ ->
        Ok(
            (fun presented ->
                { baseline with
                    Authorize = Proxy.Policy.sharedSecret secret (fun _ -> presented) }),
            $"shared secret ({SecretHeader})"
        )
    | None, Some "1" -> Ok((fun _ -> baseline), "ANONYMOUS — every caller is served")
    | None, _ ->
        Error(
            "this route holds a BYOK key. Set FUARAN_SAMPLE_SECRET to the secret callers must present in the "
            + $"{SecretHeader} header, or FUARAN_SAMPLE_ANONYMOUS=1 if you genuinely mean to serve everyone."
        )

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

    let policyFor, describedPolicy =
        match buildPolicy () with
        | Ok(policyFor, description) -> policyFor, description
        | Error reason ->
            eprintfn "Fuaran SDK integration sample — refusing to start: %s" reason
            exit 78 // EX_CONFIG

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

                let presented =
                    match ctx.Request.Headers.TryGetValue SecretHeader with
                    | true, values ->
                        match values.ToString() with
                        | "" -> None
                        | value -> Some value
                    | _ -> None

                // The remote address is the weakest useful caller key, and the
                // sample says so: behind a proxy every caller shares one. A real
                // deployment keys on whatever it already authenticates.
                let caller =
                    Proxy.CallerId(
                        match ctx.Connection.RemoteIpAddress with
                        | null -> "unknown"
                        | address -> address.ToString()
                    )

                let! status, payload = Proxy.handle (policyFor presented) caller client body |> Async.StartAsTask

                ctx.Response.StatusCode <- status
                ctx.Response.ContentType <- "application/json"
                do! ctx.Response.WriteAsync payload
            }
            :> System.Threading.Tasks.Task)
    )
    |> ignore

    printfn "Fuaran SDK integration sample — proxying to %s" describedEndpoint
    printfn "  authorisation: %s" describedPolicy

    printfn
        "  prompt cap: %d characters · rate limit: %d turns / %g s per caller"
        Proxy.Policy.DefaultMaxPromptLength
        Proxy.Policy.DefaultRateLimit.MaxTurns
        Proxy.Policy.DefaultRateLimit.Window.TotalSeconds

    printfn "Listening on http://localhost:14040"
    app.Run "http://localhost:14040"
    0
