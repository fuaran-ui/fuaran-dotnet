module ServerDriven.Sample.Program

open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Fuaran.UI.Renderer
open Fuaran.UI.Renderer.Server
open Fuaran.UI.ServerDriven
open Fuaran.UI.ServerDriven.Driver
open Fuaran.UI.ServerDriven.AspNetCore.Endpoints
open ServerDriven.Sample

// ─── The server-driven sample host (Phase 152 Track E) ──────────────
//
// One ASP.NET app that:
//   GET /                    — server-renders the initial page (the SSR first
//                              paint the shim then patches onto).
//   GET /fuaran-live-patch.js — the generic shim (copied to wwwroot, served by
//                              UseStaticFiles).
//   GET /live/stream         — the SSE push stream (mapFuaranLive).
//   POST /live/event         — the client→server event endpoint (mapFuaranLive).
//
// No client bundle, no Fable compile — the whole interactive loop runs on the
// server and ships DomPatches. Run: `dotnet run`; open http://localhost:14050.

/// The host's renderFragment: the server renderer with empty binding sources
/// (the sample uses literal text + Static bindings — no Query/Filter/State).
let private renderFragment = Render.render BindingResolver.empty

/// A fresh per-connection session, all starting at the initial model —
/// matching the GET / SSR baseline so the first diff is exact.
///
/// `createPermissive` since Phase 782: the dispatch gate DENIES by default now,
/// and this sample drives a hand-authored tree it wrote itself. A real host
/// serving decoded trees supplies its own allow-list —
/// `{ DriverServices.create renderFragment with CanDispatch = … }` — rather than
/// naming the permissive constructor.
let private makeSession () =
    init (DriverServices.createPermissive renderFragment) App.update App.view App.initial

/// The form sample's per-connection session (Phase 152 form policy).
let private makeFormSession () =
    init (DriverServices.createPermissive renderFragment) Form.update Form.view Form.initial

let private page (streamPath: string) (sendPath: string) (bodyHtml: string) : string =
    sprintf
        """<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8" />
  <meta name="viewport" content="width=device-width, initial-scale=1" />
  <title>Fuaran server-driven sample</title>
  <link rel="stylesheet" href="/fuaran-reference.css" />
</head>
<body>
  <div id="app">%s</div>
  <p><a href="/">Counter</a> · <a href="/form">Form</a></p>
  <script src="/fuaran-live-patch.js"
          data-fuaran-live-stream="%s"
          data-fuaran-live-send="%s"></script>
</body>
</html>"""
        bodyHtml
        streamPath
        sendPath

[<EntryPoint>]
let main argv =
    // The two static assets (the shim + reference CSS) are copied next to the
    // built app under `wwwroot/` (the fsproj Content links). Point the web root
    // at the OUTPUT `wwwroot` so `dotnet run` serves them regardless of the
    // content-root layout.
    let builder =
        WebApplication.CreateBuilder(
            WebApplicationOptions(
                Args = argv,
                WebRootPath = System.IO.Path.Combine(System.AppContext.BaseDirectory, "wwwroot")
            )
        )

    let app = builder.Build()

    app.UseStaticFiles() |> ignore

    // The counter SSR first paint — render model 0's tree to HTML, wrap in page.
    app.MapGet(
        "/",
        RequestDelegate(fun ctx ->
            let bodyHtml = renderFragment (App.view App.initial)
            ctx.Response.ContentType <- "text/html; charset=utf-8"
            ctx.Response.WriteAsync(page "/live/stream" "/live/event" bodyHtml))
    )
    |> ignore

    // The form SSR first paint (Phase 152 form policy) at /form.
    app.MapGet(
        "/form",
        RequestDelegate(fun ctx ->
            let bodyHtml = renderFragment (Form.view Form.initial)
            ctx.Response.ContentType <- "text/html; charset=utf-8"
            ctx.Response.WriteAsync(page "/form/live/stream" "/form/live/event" bodyHtml))
    )
    |> ignore

    // The counter SSE+POST live endpoints (default /live/* paths).
    mapFuaranLive app (defaultConfig makeSession) |> ignore

    // The form's own live endpoints under /form/live/*, with Phase 156 declared
    // form validation enabled on each connection (server-side range/required
    // enforcement on submit — the trust floor) via the per-connection hook.
    let formConfig =
        { defaultConfig makeFormSession with
            StreamPath = "/form/live/stream"
            EventPath = "/form/live/event"
            CookieName = "fuaran-form-conn"
            // Phase 211: the hook receives the resolved principal first (unused
            // here — the sample layers no auth).
            ConfigureConnection = fun _principal conn -> conn.EnableFormValidation FormValidation.declaredOnly }

    mapFuaranLive app formConfig |> ignore

    app.Run("http://localhost:14050")
    0
