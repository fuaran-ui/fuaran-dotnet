module Fuaran.UI.ServerDriven.WebSocket.Endpoints

open System
open System.IO
open System.Net.WebSockets
open System.Text
open System.Threading.Tasks
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Routing
open Microsoft.AspNetCore.Builder
open Fuaran.UI.ServerDriven
open Fuaran.UI.ServerDriven.Driver

// ============================================================================
//  WebSocket endpoint wiring (Phase 152, Track D — Backend 2).
//
//  One bidirectional channel: accept the WS, build a fresh session +
//  `WsChannel` + `LiveConnection`, then run two loops on the one socket:
//    - send pump   — drain the channel's frame queue → `ws.SendAsync` (a JSON
//                    text message per frame, `FrameWire.encodeJson`).
//    - receive loop — `ws.ReceiveAsync` → `Inbound.tryParseLiveEvent` →
//                    `conn.Handle` (→ G1 → driver → frames back via the pump).
//  When either loop ends (client close / abort), close the channel.
//
//  The host must enable WebSockets (`app.UseWebSockets()`). WS owns its own
//  reconnect + resequencing client-side (no EventSource freebie) — the
//  `Frame.Seq` + `LiveConnection.Resync` give the server half; the shim's WS
//  adapter (a Track-B drop-in) owns the client backoff.
// ============================================================================

/// The default principal resolver: the authenticated user's name, or `""` when
/// the request is unauthenticated (no auth layered, or auth fail-open). Null-safe
/// against a missing `Identity`. Deliberately identical to the SSE backend's
/// resolver — the transport-parity test pins the two together.
let defaultResolvePrincipal (ctx: HttpContext) : string =
    match ctx.User.Identity with
    | null -> ""
    | identity when not identity.IsAuthenticated -> ""
    | identity -> identity.Name |> Option.ofObj |> Option.defaultValue ""

/// Per-app WS live config.
///
/// `MakeSession` builds a fresh `LiveSession` per connection.
///
/// `TokenPath` (Phase 787) is the mint endpoint. The WS upgrade is a single
/// request that both opens and authorises the session, so there is no second
/// request to gate the way the SSE backend gates its POST — the token must
/// therefore be issued BEFORE the upgrade. A `GET TokenPath` resolves the
/// principal, mints a bound `connId`, sets the correlation cookie and answers
/// 204; the client then opens the socket, which carries the cookie. That gives
/// the two transports the same two-endpoint shape: one endpoint mints the
/// principal-bound token, the other refuses anything that does not carry it.
///
/// `CookieName` is where that token rides. It defaults to `fuaran-conn-ws`,
/// distinct from the SSE backend's `fuaran-conn`, so a host running both
/// transports does not have one clobber the other's connId.
///
/// `Secret` (Phase 787) is the HMAC key the connId token is signed with, and
/// `ResolvePrincipal` resolves the identity it is bound to — both exactly as
/// `LiveAppConfig` carries them, and both gating through the same
/// `Fuaran.UI.ServerDriven.ConnToken`. `Secret` defaults to a fresh per-process
/// key: secure by default, but not restart-stable / multi-node; a host that
/// needs those supplies its own. **The host must layer auth in front of the WS
/// paths** — the resolver reflects whatever principal that auth established.
///
/// `MaxMessageBytes` (Phase 787) bounds the fragment accumulator. A single
/// inbound live event is a small JSON envelope; a client streaming endless
/// fragments would otherwise grow server memory without limit, because the
/// accumulator only flushes at `EndOfMessage`. Defaults to the SSE path's 1 MB
/// body cap (`LiveLimits.defaultMaxInboundBytes`, the one place the two
/// transports' budgets are written down). Exceeding it closes the socket with
/// `MessageTooBig` (1009).
type LiveWsConfig<'Model, 'Msg> =
    { Path: string
      TokenPath: string
      CookieName: string
      Secret: byte[]
      ResolvePrincipal: HttpContext -> string
      MaxMessageBytes: int64
      MakeSession: unit -> LiveSession<'Model, 'Msg> }

/// Default WS path (`/live/ws`) + token mint path (`/live/ws-token`) + cookie
/// (`fuaran-conn-ws`) + a fresh per-process HMAC secret + the default principal
/// resolver + the SSE path's 1 MB message cap.
let defaultWsConfig (makeSession: unit -> LiveSession<'Model, 'Msg>) : LiveWsConfig<'Model, 'Msg> =
    { Path = "/live/ws"
      TokenPath = "/live/ws-token"
      CookieName = "fuaran-conn-ws"
      Secret = ConnToken.freshSecret ()
      ResolvePrincipal = defaultResolvePrincipal
      MaxMessageBytes = LiveLimits.defaultMaxInboundBytes
      MakeSession = makeSession }

/// Mint a principal-bound connection token and set it as the correlation cookie
/// (Phase 787). The cookie is hardened exactly as the SSE backend hardens its
/// own: HttpOnly (no script access), Secure (HTTPS only), SameSite=Strict.
///
/// SameSite=Strict is what closes cross-site WebSocket hijacking here. A browser
/// applies no same-origin policy to a WS handshake, so an attacker page can open
/// a socket to this server; with a Strict cookie the handshake carries no token,
/// so the upgrade is refused. The Go helper closes the same class from the other
/// side with an Origin allowlist — two hosts, one threat, and neither relies on
/// the other.
let private tokenHandler (config: LiveWsConfig<'Model, 'Msg>) (ctx: HttpContext) : Task =
    task {
        let principal = config.ResolvePrincipal ctx
        let connId = Guid.NewGuid().ToString("N")
        let token = ConnToken.sign config.Secret principal connId

        ctx.Response.Cookies.Append(
            config.CookieName,
            token,
            CookieOptions(HttpOnly = true, Secure = true, SameSite = SameSiteMode.Strict)
        )

        ctx.Response.StatusCode <- 204
    }
    :> Task

/// Run one accepted socket: the send pump + receive loop over a fresh session,
/// under the `connId` the verified token bound to this principal (Phase 787).
let private runSocket (config: LiveWsConfig<'Model, 'Msg>) (ctx: HttpContext) (connId: string) : Task =
    task {
        let! ws = ctx.WebSockets.AcceptWebSocketAsync()
        let channel = WsChannel()
        let session = config.MakeSession()
        let conn = LiveConnection(connId, session, channel)
        let token = ctx.RequestAborted

        let sendPump =
            task {
                let reader = channel.Reader

                try
                    // Drain until the client disconnects or the queue completes (a
                    // host `Close`, or the Phase 212 bounded-overflow close — the
                    // pump must END then, so the socket closes and the client
                    // reconnect-replays, rather than spin on a completed queue).
                    // The `Frame` binding is scoped inside the `TryRead` success
                    // arm — no `Unchecked.defaultof` sentinel (Phase 212).
                    let mutable pumping = true

                    while pumping && not token.IsCancellationRequested do
                        let! more = reader.WaitToReadAsync(token).AsTask()

                        if more then
                            let mutable pending = true

                            while pending do
                                match reader.TryRead() with
                                | true, frame ->
                                    let bytes = Encoding.UTF8.GetBytes(FrameWire.encodeJson frame)

                                    do! ws.SendAsync(ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, token)
                                | false, _ -> pending <- false
                        else
                            pumping <- false // queue completed — end the pump
                with :? OperationCanceledException ->
                    ()
            }

        let recvLoop =
            task {
                let buffer = Array.zeroCreate<byte> 8192
                // Accumulate fragmented frames here until EndOfMessage; a single
                // logical message can arrive across multiple ReceiveAsync chunks.
                use ms = new MemoryStream()

                try
                    let mutable go = true

                    while go && not token.IsCancellationRequested do
                        let! result = ws.ReceiveAsync(ArraySegment<byte>(buffer), token)

                        if result.MessageType = WebSocketMessageType.Close then
                            go <- false
                        elif ms.Length + int64 result.Count > config.MaxMessageBytes then
                            // Phase 787 — bound the accumulator. Checked BEFORE the
                            // write, so the over-cap chunk is never buffered: a client
                            // streaming endless fragments is closed at the budget, not
                            // one chunk past it. `MessageTooBig` (1009) is the typed
                            // reason, so a client sees a protocol-level cause rather
                            // than a bare disconnect. This is the SSE path's 1 MB
                            // `413` expressed in the vocabulary this transport has.
                            eprintfn
                                "[Fuaran] WebSocket inbound message exceeded %d bytes on conn %s; closing"
                                config.MaxMessageBytes
                                connId

                            do!
                                ws.CloseAsync(
                                    WebSocketCloseStatus.MessageTooBig,
                                    "inbound message exceeds the configured budget",
                                    token
                                )

                            go <- false
                        else
                            ms.Write(buffer, 0, result.Count)

                            // Only decode + parse once the full message has arrived.
                            if result.EndOfMessage then
                                let json = Encoding.UTF8.GetString(ms.GetBuffer(), 0, int ms.Length)
                                ms.SetLength 0L // reset for the next message

                                // A malformed payload must not tear down the receive
                                // loop. The shared guarded parse (Phase 211) returns
                                // None for a non-JSON body — skip it, keep the socket
                                // alive; both transports fail identically here. The
                                // try/with stays as defence-in-depth for a downstream
                                // Handle throw; cancellation still propagates.
                                try
                                    match Inbound.tryParseLiveEvent connId json with
                                    | Some ev -> conn.Handle ev
                                    | None ->
                                        eprintfn
                                            "[Fuaran] WebSocket inbound parse failed for conn %s; skipping malformed message"
                                            connId
                                with ex when not (ex :? OperationCanceledException) ->
                                    eprintfn
                                        "[Fuaran] WebSocket inbound handling failed for conn %s; skipping message: %s"
                                        connId
                                        ex.Message
                with :? OperationCanceledException ->
                    ()
            }

        let! _ = Task.WhenAny(sendPump, recvLoop)
        (channel :> IFuaranLiveChannel).Close()
    }
    :> Task

let private handle (config: LiveWsConfig<'Model, 'Msg>) (ctx: HttpContext) : Task =
    task {
        if not ctx.WebSockets.IsWebSocketRequest then
            ctx.Response.StatusCode <- 400
        else
            // Verify the signed connId token against the request principal
            // (Phase 787) BEFORE accepting the socket. Refusing pre-accept is
            // load-bearing: once `AcceptWebSocketAsync` has run the response has
            // been committed as a 101 and there is no status code left to send,
            // so a post-accept check can only close an already-established
            // socket. The same ordering lesson the Go helper's Origin check
            // records — validate while the response is still yours to write.
            //
            // A missing / forged / cross-principal cookie yields no connId and
            // is refused 401, never upgraded. The connId is then TAKEN FROM THE
            // TOKEN rather than freshly minted, which is what binds this socket
            // to the principal the token was issued to.
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
            | Some connId -> do! runSocket config ctx connId
    }
    :> Task

/// Map the Fuaran WebSocket live endpoints onto an `IEndpointRouteBuilder`. The
/// host must have called `app.UseWebSockets()`.
///
/// TWO endpoints since Phase 787, mirroring the SSE backend's shape: `TokenPath`
/// mints the principal-bound connection token, `Path` upgrades to the socket and
/// refuses anything not carrying it. A client opens the socket by fetching the
/// token first (same-origin, credentials included) and then connecting.
let mapFuaranLiveWebSocket (app: IEndpointRouteBuilder) (config: LiveWsConfig<'Model, 'Msg>) : unit =
    app.MapGet(config.TokenPath, RequestDelegate(tokenHandler config)) |> ignore

    app.MapGet(config.Path, RequestDelegate(handle config)) |> ignore
