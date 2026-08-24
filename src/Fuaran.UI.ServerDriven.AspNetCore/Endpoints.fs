module Fuaran.UI.ServerDriven.AspNetCore.Endpoints

open System
open System.IO
open System.Threading.Tasks
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Routing
open Microsoft.AspNetCore.Builder
open Microsoft.Extensions.Logging
open Microsoft.Extensions.Primitives
open Fuaran.UI.ServerDriven
open Fuaran.UI.ServerDriven.Validation
open Fuaran.UI.ServerDriven.Driver

// ============================================================================
//  Endpoint wiring (Phase 152, Track D — SSE+POST backend, the v1 default).
//
//  Two endpoints, the thin glue around the tested core (`FrameWire`,
//  `LiveConnection`, `Inbound`, `ConnectionRegistry`):
//
//   GET  StreamPath  — opens the SSE stream. Assigns a connId, binds it to the
//        request principal as a signed token (`ConnToken.sign`, Phase 211), sets
//        the correlation cookie, builds a fresh session + `SseChannel` +
//        `LiveConnection` (composing a default logging sink onto the session's
//        `OnReject` seam, Phase 212 — denials are recorded by default),
//        registers it, then drains the channel's frame queue to the long-lived
//        response (`FrameWire.encodeSse` + flush) until the client disconnects
//        or the bounded queue closes (Phase 212 — a stalled reader is closed,
//        not buffered without limit). On disconnect: deregister + close.
//        `Last-Event-ID` (the client's last-applied `Frame.Seq`) drives `Resync`.
//   POST EventPath   — receives one client event. Verifies the signed connId
//        token against the request principal (`ConnToken.verify`, Phase 211;
//        401 on a forged / unbound cookie), guard-parses the body
//        (`Inbound.tryParseLiveEvent`; 400 on a malformed body), then routes it
//        to the registered connection (`Handle` → G1 → driver → frames pushed
//        back down the GET stream). Returns 204 on success.
//
//  No transport type leaks into the core — this module is the only place that
//  knows about ASP.NET. A WebSocket backend is the parallel package.
// ============================================================================

/// Per-app live config. `MakeSession` builds a fresh `LiveSession` per
/// connection (the host wires `RenderFragment` to `Renderer.Server.Render.render`
/// + `CanDispatch` to its policy gate).
///
/// `ResolvePrincipal` (Phase 211) resolves the request's authenticated user id
/// from the `HttpContext` — it is called at stream-open (to bind the connId to
/// the user) and on every inbound POST (to verify that binding). The default
/// reads `ctx.User.Identity.Name` for an authenticated request, `""` otherwise;
/// a host with a richer identity (tenant + user, a claim) supplies its own.
/// **The host must layer auth in front of `/live/*`** — this resolver reflects
/// whatever principal that auth established.
///
/// `Secret` (Phase 211) is the HMAC key the connId token is signed with. It
/// defaults to a fresh per-process key (`ConnToken.freshSecret ()`) — secure by
/// default, but not restart-stable / multi-node; a host that needs those
/// supplies its own stable secret.
///
/// `ConfigureConnection` is the per-connection opt-in hook the host uses to call
/// the `LiveConnection.Enable*` seams (`EnableFormValidation` / `EnableRouting` /
/// `EnableDurability` / `EnableTelemetry`). It runs once on each freshly-built
/// connection, before the first inbound event, and (Phase 211) receives the
/// **resolved principal** so the host can thread it into durability
/// (`conn.EnableDurability(store, userId = principal)`), stamping the audit
/// journal's `OpRecord.UserId` with the real user instead of a placeholder.
/// Defaults to a no-op (the plain 152 path).
/// `MaxBodyBytes` (Phase 787) caps ONE inbound POST body. Phase 211 introduced
/// this cap as a literal inside the handler; it is a config field now for the
/// same reason `LiveWsConfig` carries one — a budget written inside a handler
/// cannot be compared with the other transport's, so parity between them was
/// unassertable. Defaults to `LiveLimits.defaultMaxInboundBytes` (1 MB), the
/// single place both transports read it from.
type LiveAppConfig<'Model, 'Msg> =
    { StreamPath: string
      EventPath: string
      CookieName: string
      Secret: byte[]
      ResolvePrincipal: HttpContext -> string
      MaxBodyBytes: int64
      MakeSession: unit -> LiveSession<'Model, 'Msg>
      ConfigureConnection: string -> LiveConnection<'Model, 'Msg> -> unit }

/// The default principal resolver: the authenticated user's name, or `""` when
/// the request is unauthenticated (no auth layered, or auth fail-open). Null-safe
/// against a missing `Identity`.
let defaultResolvePrincipal (ctx: HttpContext) : string =
    match ctx.User.Identity with
    | null -> ""
    | identity when not identity.IsAuthenticated -> ""
    | identity -> identity.Name |> Option.ofObj |> Option.defaultValue ""

/// Default paths (`/live/stream`, `/live/event`) + cookie (`fuaran-conn`) + a
/// fresh per-process HMAC secret + the default principal resolver + a no-op
/// connection configurator.
let defaultConfig (makeSession: unit -> LiveSession<'Model, 'Msg>) : LiveAppConfig<'Model, 'Msg> =
    { StreamPath = "/live/stream"
      EventPath = "/live/event"
      CookieName = "fuaran-conn"
      Secret = ConnToken.freshSecret ()
      ResolvePrincipal = defaultResolvePrincipal
      MaxBodyBytes = LiveLimits.defaultMaxInboundBytes
      MakeSession = makeSession
      ConfigureConnection = fun _ _ -> () }

let private streamHandler
    (config: LiveAppConfig<'Model, 'Msg>)
    (registry: ConnectionRegistry)
    (ctx: HttpContext)
    : Task =
    task {
        // Bind the connId to the request principal (Phase 211): the cookie value
        // is the HMAC-signed token, not a bare GUID, so the POST handler can
        // reject a forged / cross-principal id rather than routing it.
        let principal = config.ResolvePrincipal ctx
        let connId = Guid.NewGuid().ToString("N")
        let token = ConnToken.sign config.Secret principal connId
        ctx.Response.ContentType <- "text/event-stream"
        ctx.Response.Headers["Cache-Control"] <- StringValues "no-cache"
        // Disable proxy buffering so frames flush promptly (nginx + friends).
        ctx.Response.Headers["X-Accel-Buffering"] <- StringValues "no"
        // The signed token is the session-routing authz credential (the POST
        // handler reads it back from this cookie and verifies it to route inbound
        // events). Harden the cookie transport too: HttpOnly (no script access),
        // Secure (HTTPS only), SameSite=Strict (no cross-site send) so it cannot
        // be lifted by injected script or replayed cross-origin.
        ctx.Response.Cookies.Append(
            config.CookieName,
            token,
            CookieOptions(HttpOnly = true, Secure = true, SameSite = SameSiteMode.Strict)
        )

        let channel = SseChannel()

        // Phase 212 — denials are recorded by default. Compose a logging sink
        // onto the session's `OnReject` seam so a G1 rejection reaches the
        // host's logger (via `RejectReason.describe` — log-safe, no payload
        // values) even when the host wired nothing; the host's own sink (if
        // any) still fires. Falls back to stderr when no `ILoggerFactory` is
        // registered (mirrors the WebSocket backend's parse-failure posture).
        let logReject =
            match ctx.RequestServices.GetService typeof<ILoggerFactory> with
            | :? ILoggerFactory as lf ->
                let logger = lf.CreateLogger "Fuaran.UI.ServerDriven"

                fun (reason: RejectReason) ->
                    logger.LogWarning(
                        "Fuaran live event rejected on conn {ConnId}: {Reason}",
                        connId,
                        RejectReason.describe reason
                    )
            | _ ->
                fun reason ->
                    eprintfn "[Fuaran] live event rejected on conn %s: %s" connId (RejectReason.describe reason)

        let session =
            let made = config.MakeSession()
            let hostOnReject = made.Services.OnReject

            { made with
                Services =
                    { made.Services with
                        OnReject =
                            fun reason ->
                                logReject reason
                                hostOnReject reason } }

        let conn = LiveConnection(connId, session, channel)
        // Host opt-in seams (form validation / routing / durability / telemetry),
        // applied before the connection sees its first event. The resolved
        // principal is threaded in (Phase 211) so the host can attribute the
        // durability journal to the real user.
        config.ConfigureConnection principal conn
        registry.Add(connId, toHandle conn channel)

        // Reconnect replay: EventSource resends the last-seen id as
        // Last-Event-ID; replay the frames the client missed.
        match ctx.Request.Headers.TryGetValue "Last-Event-ID" with
        | true, v ->
            match Int32.TryParse(string v) with
            | true, lastSeq -> conn.Resync lastSeq |> ignore
            | _ -> ()
        | _ -> ()

        let reader = channel.Reader
        let token = ctx.RequestAborted

        try
            do! ctx.Response.Body.FlushAsync token

            // Drain until the client disconnects or the queue completes (a
            // host `Close`, or the Phase 212 bounded-overflow close — the
            // stream must END then, so the client reconnect-replays, rather
            // than spin on a completed queue). The `Frame` binding is scoped
            // inside the `TryRead` success arm — no `Unchecked.defaultof`
            // sentinel (Phase 212).
            let mutable streaming = true

            while streaming && not token.IsCancellationRequested do
                let! more = reader.WaitToReadAsync(token).AsTask()

                if more then
                    let mutable pending = true

                    while pending do
                        match reader.TryRead() with
                        | true, frame ->
                            do! ctx.Response.WriteAsync(FrameWire.encodeSse frame, token)
                            do! ctx.Response.Body.FlushAsync token
                        | false, _ -> pending <- false
                else
                    streaming <- false // queue completed — end the stream
        with :? OperationCanceledException ->
            () // client disconnected — normal teardown

        registry.Remove connId
        (channel :> IFuaranLiveChannel).Close()
    }
    :> Task

let private eventHandler
    (config: LiveAppConfig<'Model, 'Msg>)
    (registry: ConnectionRegistry)
    (ctx: HttpContext)
    : Task =
    task {
        // Verify the signed connId token against the request principal (Phase 211,
        // C1): a missing / forged / cross-principal cookie yields no connId and is
        // rejected 401, never routed. The registry lookup is no longer the entire
        // gate — the cryptographic binding is.
        let principal = config.ResolvePrincipal ctx

        let boundConnId =
            match ctx.Request.Cookies.TryGetValue config.CookieName with
            | true, token ->
                match token with
                | null -> None
                | t -> ConnToken.verify config.Secret principal t
            | _ -> None

        match boundConnId with
        | None -> ctx.Response.StatusCode <- 401 // Unauthorized — unbound / forged connId
        | Some connId ->
            // Guard against an unbounded buffered read: a single inbound live event
            // is a small JSON envelope, so reject anything past a sane cap (1 MB)
            // with 413 before allocating the body. A client that advertises an
            // over-cap Content-Length is rejected outright; bodies with no declared
            // length fall through to the read (the StreamReader path) — those are
            // bounded by Kestrel's own MaxRequestBodySize.
            let maxBodyBytes = config.MaxBodyBytes

            match ctx.Request.ContentLength |> Option.ofNullable with
            | Some len when len > maxBodyBytes -> ctx.Response.StatusCode <- 413 // Payload Too Large
            | _ ->
                use sr = new StreamReader(ctx.Request.Body)
                let! body = sr.ReadToEndAsync()

                // Guarded parse (Phase 211, C2/M1): a malformed body is a clean 400,
                // never an unhandled exception; both transports share this seam.
                match Inbound.tryParseLiveEvent connId body with
                | None -> ctx.Response.StatusCode <- 400 // Bad Request — malformed body
                | Some ev ->
                    match registry.TryGet connId with
                    | Some conn -> conn.Handle ev
                    | None -> () // no live connection for this id — drop (default-deny)

                    ctx.Response.StatusCode <- 204
    }
    :> Task

/// Map the Fuaran live endpoints onto an `IEndpointRouteBuilder`. Returns the
/// `ConnectionRegistry` (for the host to inspect live-connection count / close).
let mapFuaranLive (app: IEndpointRouteBuilder) (config: LiveAppConfig<'Model, 'Msg>) : ConnectionRegistry =
    let registry = ConnectionRegistry()

    app.MapGet(config.StreamPath, RequestDelegate(streamHandler config registry))
    |> ignore

    app.MapPost(config.EventPath, RequestDelegate(eventHandler config registry))
    |> ignore

    registry
