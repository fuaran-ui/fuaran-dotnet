namespace Fuaran.UI.ServerDriven.WebSocket

open System.Threading.Channels
open Fuaran.UI.ServerDriven
open Fuaran.UI.ServerDriven.Validation

// ============================================================================
//  WsChannel (Phase 152, Track D — WebSocket backend).
//
//  An `IFuaranLiveChannel` whose `Push` enqueues a `Frame` onto an in-process
//  queue drained by the WS send pump (which writes `FrameWire.encodeJson` as a
//  text message — no SSE `id:`/`event:` framing; WS frames are raw JSON). Same
//  shape as the SSE backend's `SseChannel` — which is exactly the point: the
//  driver / diff / lowering core sees only `IFuaranLiveChannel`, so the two
//  backends differ ONLY in their channel + endpoint glue. Building this second,
//  structurally-identical backend is the architectural-integrity check that
//  `IFuaranLiveChannel` is genuinely transport-neutral and not accidentally
//  SSE-shaped.
//
//  Unlike SSE+POST (two HTTP requests), a WebSocket is bidirectional: the same
//  socket carries both the outbound frames (this channel's queue) and the
//  inbound events (the endpoint's receive loop → `LiveConnection.Handle`).
//
//  ── Resource bound (Phase 212 tail) ─────────────────────────────────────────
//  The queue is bounded (`WsDefaults.FrameQueueCapacity`, overridable per
//  channel) — the same stalled-reader guard as the SSE backend's `SseChannel`.
//  A stalled WS peer that keeps generating frames would otherwise grow this
//  queue without limit — one connection, unbounded server memory. On overflow
//  the channel CLOSES instead of growing: the send pump observes the completed
//  queue and ends, the socket closes, and the client's WS adapter reconnects;
//  `Frame.Seq` + `LiveConnection.Resync` replay the missed frames — a clean
//  reconnect-replay, not silent frame loss.
// ============================================================================

/// Documented caps for the WebSocket backend (Phase 212 tail).
[<RequireQualifiedAccess>]
module WsDefaults =
    /// Default bound of the per-connection WS frame queue, in frames. A queue
    /// this deep means the peer is stalled, not slow — on overflow the channel
    /// closes and the client reconnect-replays via `Frame.Seq` / `Resync`.
    [<Literal>]
    let FrameQueueCapacity = 256

/// An `IFuaranLiveChannel` backed by a bounded in-process frame queue, drained
/// by the WS send pump. `capacity` bounds the queue (default
/// `WsDefaults.FrameQueueCapacity`); a full queue closes the channel rather
/// than growing (the stalled-reader guard — see the module header).
type WsChannel(?capacity: int) =
    let frames =
        Channel.CreateBounded<Frame>(
            BoundedChannelOptions(
                defaultArg capacity WsDefaults.FrameQueueCapacity,
                FullMode = BoundedChannelFullMode.Wait
            )
        )

    let mutable handler: (LiveEvent -> unit) option = None
    let mutable closed = false

    let close () =
        closed <- true
        frames.Writer.TryComplete() |> ignore

    /// The queue reader the send pump drains.
    member _.Reader: ChannelReader<Frame> = frames.Reader

    /// True once `Close` completed the queue (host close or bounded-overflow
    /// close).
    member _.IsClosed = closed

    /// The inbound handler registered by `LiveConnection` (unused on the WS
    /// path — inbound flows through the endpoint's receive loop + `Handle`).
    member _.Handler = handler

    interface IFuaranLiveChannel with
        member _.Push(frame) =
            // With `FullMode.Wait`, `TryWrite` returns false when the queue is
            // full (it never blocks). A full queue means the WS peer is
            // stalled — close the connection instead of growing memory
            // unbounded (Phase 212 tail); the client's reconnect-replay
            // recovers.
            if not (frames.Writer.TryWrite frame) then
                close ()

        member _.Receive(h) = handler <- Some h

        member _.Close() = close ()
