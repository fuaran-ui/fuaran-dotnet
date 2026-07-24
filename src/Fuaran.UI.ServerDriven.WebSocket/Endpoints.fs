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

/// Per-app WS live config. `MakeSession` builds a fresh `LiveSession` per
/// connection (same shape as the SSE backend's config, minus the cookie — the
/// WS connection carries its own identity).
type LiveWsConfig<'Model, 'Msg> =
    { Path: string
      MakeSession: unit -> LiveSession<'Model, 'Msg> }

/// Default WS path (`/live/ws`).
let defaultWsConfig (makeSession: unit -> LiveSession<'Model, 'Msg>) : LiveWsConfig<'Model, 'Msg> =
    { Path = "/live/ws"
      MakeSession = makeSession }

let private handle (config: LiveWsConfig<'Model, 'Msg>) (ctx: HttpContext) : Task =
    task {
        if not ctx.WebSockets.IsWebSocketRequest then
            ctx.Response.StatusCode <- 400
        else
            let! ws = ctx.WebSockets.AcceptWebSocketAsync()
            let connId = Guid.NewGuid().ToString("N")
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

                                        do!
                                            ws.SendAsync(
                                                ArraySegment<byte>(bytes),
                                                WebSocketMessageType.Text,
                                                true,
                                                token
                                            )
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

/// Map the Fuaran WebSocket live endpoint onto an `IEndpointRouteBuilder`. The
/// host must have called `app.UseWebSockets()`.
let mapFuaranLiveWebSocket (app: IEndpointRouteBuilder) (config: LiveWsConfig<'Model, 'Msg>) : unit =
    app.MapGet(config.Path, RequestDelegate(handle config)) |> ignore
