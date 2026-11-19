module Fuaran.UI.ServerDriven.AspNetCore.Endpoints

open System
open System.IO
open System.Threading
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
//        not buffered without limit). When the stream ends, for any reason, its
//        queue is closed and it detaches from the connection (Phase 1928).
//        A GET carrying `Last-Event-ID` (the client's last-applied `Frame.Seq`)
//        whose re-verified cookie names a registered connection RE-ATTACHES to
//        it — the session the client had — and replays what it missed, or
//        re-baselines it when the replay buffer no longer holds all of it.
//        An optional tab id (`?tab=`) scopes the connection to one tab.
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

/// Phase 2170 — `defaultConfig` for a host that maps with `mapFuaranLiveAsync`.
/// Every default is `defaultConfig`'s; `MakeSession` is never called on that
/// path (the async map takes its own factory), and refuses loudly if a
/// synchronous map is handed this config by mistake.
let defaultAsyncConfig<'Model, 'Msg> () : LiveAppConfig<'Model, 'Msg> =
    defaultConfig (fun () ->
        invalidOp
            "this LiveAppConfig was built by defaultAsyncConfig: map it with mapFuaranLiveAsync, which awaits its own session factory")

/// Phase 1928 — the query parameter naming the tab a stream / event belongs to.
[<Literal>]
let TabQueryParameter = "tab"

/// Phase 1928 — the header naming the tab an event belongs to (a `fetch` POST
/// can send a header; an `EventSource` GET cannot, so it uses the query).
[<Literal>]
let TabHeader = "X-Fuaran-Tab"

/// The tab id a request names: the `tab` query parameter, else the
/// `X-Fuaran-Tab` header. `Ok None` when it names none; `Error ()` for a value
/// that is not 1–64 characters of `[A-Za-z0-9_-]` (a repeated parameter
/// included), which the endpoints refuse 400.
let private tryTab (ctx: HttpContext) : Result<string option, unit> =
    let raw =
        match ctx.Request.Query.TryGetValue TabQueryParameter with
        | true, v when v.Count > 0 -> Some(string v)
        | _ ->
            match ctx.Request.Headers.TryGetValue TabHeader with
            | true, v when v.Count > 0 -> Some(string v)
            | _ -> None

    let wellFormed (t: string) =
        t.Length >= 1
        && t.Length <= 64
        && t |> Seq.forall (fun c -> Char.IsAsciiLetterOrDigit c || c = '-' || c = '_')

    match raw with
    | None -> Ok None
    | Some t when wellFormed t -> Ok(Some t)
    | Some _ -> Error()

/// The id the request's correlation cookie binds to the request principal
/// (Phase 211): `None` for a missing, forged or cross-principal cookie.
let private boundId (config: LiveAppConfig<'Model, 'Msg>) (principal: string) (ctx: HttpContext) : string option =
    match ctx.Request.Cookies.TryGetValue config.CookieName with
    | true, token ->
        match token with
        | null -> None
        | t -> ConnToken.verify config.Secret principal t
    | _ -> None

/// The registry key of a connection: the cookie-bound id, scoped by the tab
/// when the request names one (Phase 1928). A minted id is 32 hex digits, so
/// no tabless key can equal a tabbed one.
let private connKey (boundId: string) (tab: string option) =
    match tab with
    | None -> boundId
    | Some t -> boundId + "." + t

/// The client's last-applied `Frame.Seq`, from `Last-Event-ID`.
let private lastEventId (ctx: HttpContext) : int option =
    match ctx.Request.Headers.TryGetValue "Last-Event-ID" with
    | true, v ->
        match Int32.TryParse(string v) with
        | true, lastSeq -> Some lastSeq
        | _ -> None
    | _ -> None

let private streamHeaders (ctx: HttpContext) =
    ctx.Response.ContentType <- "text/event-stream"
    ctx.Response.Headers["Cache-Control"] <- StringValues "no-cache"
    // Disable proxy buffering so frames flush promptly (nginx + friends).
    ctx.Response.Headers["X-Accel-Buffering"] <- StringValues "no"

/// Drain `channel` to the response until the client disconnects or the queue
/// completes (a host `Close`, a re-attach that took the connection over, or the
/// Phase 212 bounded-overflow close — the stream must END then, so the client
/// reconnect-replays, rather than spin on a completed queue). Whatever ends it
/// — a write that fails included — the queue is closed and `stream` detaches
/// from the connection in `finally` (Phase 1928), so nothing outlives the
/// stream but the connection's reconnect grace.
let private drain
    (ctx: HttpContext)
    (registry: ConnectionRegistry)
    (connId: string)
    (stream: obj)
    (channel: SseChannel)
    : Task =
    task {
        let reader = channel.Reader
        let token = ctx.RequestAborted

        try
            try
                do! ctx.Response.Body.FlushAsync token

                // The `Frame` binding is scoped inside the `TryRead` success arm
                // — no `Unchecked.defaultof` sentinel (Phase 212).
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
        finally
            (channel :> IFuaranLiveChannel).Close()
            registry.Detach(connId, stream)
    }
    :> Task

/// One live SSE stream over a freshly opened session (and its effect outbox,
/// when the session's update returns awaited effects).
let private serveStream
    (config: LiveAppConfig<'Model, 'Msg>)
    (registry: ConnectionRegistry)
    (ctx: HttpContext)
    (principal: string)
    (cookieId: string option)
    (tab: string option)
    (lastSeq: int option)
    (made: LiveSession<'Model, 'Msg>)
    (outbox: EffectOutbox<'Msg> option)
    : Task =
    task {
        // Bind the id to the request principal (Phase 211): the cookie value is
        // the HMAC-signed token, not a bare GUID, so the POST handler can reject
        // a forged / cross-principal id rather than routing it. A tabless stream
        // mints a fresh id per connection (one stream per browser: the last one
        // opened routes the POSTs); a stream naming its tab keeps the id the
        // cookie already carries (Phase 1928), so the tabs of one browser share
        // one principal-bound id and are told apart by their tab.
        let boundTo =
            match tab, cookieId with
            | Some _, Some id -> id
            | _ -> Guid.NewGuid().ToString("N")

        let connId = connKey boundTo tab
        streamHeaders ctx
        // The signed token is the session-routing authz credential (the POST
        // handler reads it back from this cookie and verifies it to route inbound
        // events). Harden the cookie transport too: HttpOnly (no script access),
        // Secure (HTTPS only), SameSite=Strict (no cross-site send) so it cannot
        // be lifted by injected script or replayed cross-origin.
        ctx.Response.Cookies.Append(
            config.CookieName,
            ConnToken.sign config.Secret principal boundTo,
            CookieOptions(HttpOnly = true, Secure = true, SameSite = SameSiteMode.Strict)
        )

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

        // Phase 2170 — an awaited effect that faults is reported, never lost.
        let logEffectFault =
            match ctx.RequestServices.GetService typeof<ILoggerFactory> with
            | :? ILoggerFactory as lf ->
                let logger = lf.CreateLogger "Fuaran.UI.ServerDriven"

                fun (ex: exn) -> logger.LogError(ex, "Fuaran live effect failed on conn {ConnId}", connId)
            | _ -> fun ex -> eprintfn "[Fuaran] live effect failed on conn %s: %s" connId ex.Message

        let session =
            let hostOnReject = made.Services.OnReject

            { made with
                Services =
                    { made.Services with
                        OnReject =
                            fun reason ->
                                logReject reason
                                hostOnReject reason } }

        // The connection outlives this stream (Phase 1928): it is built over a
        // relay a reconnecting stream re-attaches to, and its effects run for the
        // CONNECTION's lifetime, which ends when the registry evicts it — not
        // when this request does.
        let relay = RelayChannel()
        // The queue THIS stream drains, taken before the connection is
        // registered: a reconnect that re-attaches later swaps in its own queue
        // and closes this one, which ends this stream rather than sharing it.
        let channel = relay.Current
        let lifetime = new CancellationTokenSource()
        let conn = LiveConnection(connId, session, relay)

        match outbox with
        | Some effects -> conn.EnableEffects(effects, logEffectFault, lifetime.Token)
        | None -> ()

        // Host opt-in seams (form validation / routing / durability / telemetry),
        // applied before the connection sees its first event. The resolved
        // principal is threaded in (Phase 211) so the host can attribute the
        // durability journal to the real user.
        config.ConfigureConnection principal conn

        let stream = obj ()

        registry.AddAttached(
            connId,
            toReattachableHandle conn relay,
            stream,
            fun () ->
                (relay :> IFuaranLiveChannel).Close()
                lifetime.Cancel()
        )

        // A client that reports a last-applied sequence is reconnecting, but not
        // to this connection (its own was evicted, or another stream holds it):
        // its DOM belongs to a session this one is not. Re-baseline it on this
        // session rather than patch a DOM the server does not describe — unless
        // the host already did (`ResumeFrom` in `ConfigureConnection`).
        if lastSeq.IsSome && conn.Sequence = 0 then
            conn.Rebaseline()

        do! drain ctx registry connId stream channel
    }
    :> Task

let private streamHandler
    (config: LiveAppConfig<'Model, 'Msg>)
    (openSession: CancellationToken -> Task<(LiveSession<'Model, 'Msg> * EffectOutbox<'Msg> option) option>)
    (registry: ConnectionRegistry)
    (ctx: HttpContext)
    : Task =
    task {
        match tryTab ctx with
        | Error() -> ctx.Response.StatusCode <- 400 // Bad Request — malformed tab id
        | Ok tab ->
            let principal = config.ResolvePrincipal ctx
            let cookieId = boundId config principal ctx
            let lastSeq = lastEventId ctx
            let stream = obj ()

            // Phase 1928 — a reconnect re-attaches to the session the client had:
            // `Last-Event-ID` present, the cookie re-verified against THIS
            // request's principal, and that id (scoped by the tab) registered.
            // No session is opened for it: the client's own is still here.
            let reattach =
                match cookieId, lastSeq with
                | Some id, Some last ->
                    let connId = connKey id tab

                    registry.TryReattach(connId, stream, tab.IsSome)
                    |> Option.map (fun conn -> connId, conn, last)
                | _ -> None

            match reattach with
            | Some(connId, conn, last) ->
                streamHeaders ctx

                let channel =
                    try
                        conn.Reattach last
                    with _ ->
                        registry.Detach(connId, stream)
                        reraise ()

                do! drain ctx registry connId stream channel
            | None ->
                // Phase 2170 — the factory is awaited FIRST, before anything is
                // written to the response, so a factory that never answers leaves
                // a request that ends with its client rather than a half-open
                // stream.
                match! openSession ctx.RequestAborted with
                | None -> () // the client went away before its session existed
                | Some(made, outbox) -> do! serveStream config registry ctx principal cookieId tab lastSeq made outbox
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
        // gate — the cryptographic binding is. The event routes to the stream of
        // the tab that sent it (Phase 1928): the cookie binds the principal, the
        // tab id selects the connection.
        let principal = config.ResolvePrincipal ctx

        match boundId config principal ctx, tryTab ctx with
        | None, _ -> ctx.Response.StatusCode <- 401 // Unauthorized — unbound / forged connId
        | Some _, Error() -> ctx.Response.StatusCode <- 400 // Bad Request — malformed tab id
        | Some id, Ok tab ->
            let connId = connKey id tab
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

    let openSession _ =
        Task.FromResult(Some(config.MakeSession(), None))

    app.MapGet(config.StreamPath, RequestDelegate(streamHandler config openSession registry))
    |> ignore

    app.MapPost(config.EventPath, RequestDelegate(eventHandler config registry))
    |> ignore

    registry

/// Phase 2170 — `mapFuaranLive` for a session factory that awaits. Identical
/// endpoints, token binding, reject sink and connection hook; the difference is
/// that the stream handler AWAITS `makeSession` before the connection exists,
/// without blocking a thread, so a host whose session state lives behind an
/// async store reads it in the factory rather than ahead of the request. When
/// the factory returns an `EffectOutbox` (a session built by
/// `Driver.initWithEffects`), the connection runs the update's awaited effects
/// in order, bound to the request's lifetime, with faults logged. A factory that
/// never answers holds only its own request. `config.MakeSession` is not called
/// (`defaultAsyncConfig` builds a config for this map).
let mapFuaranLiveAsync
    (app: IEndpointRouteBuilder)
    (config: LiveAppConfig<'Model, 'Msg>)
    (makeSession: AsyncSessionFactory<'Model, 'Msg>)
    : ConnectionRegistry =
    let registry = ConnectionRegistry()

    app.MapGet(
        config.StreamPath,
        RequestDelegate(streamHandler config (fun aborted -> AsyncSessionFactory.start aborted makeSession) registry)
    )
    |> ignore

    app.MapPost(config.EventPath, RequestDelegate(eventHandler config registry))
    |> ignore

    registry
