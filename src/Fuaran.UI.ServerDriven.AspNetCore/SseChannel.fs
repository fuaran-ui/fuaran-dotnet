namespace Fuaran.UI.ServerDriven.AspNetCore

open System.Threading.Channels
open Fuaran.UI.ServerDriven
open Fuaran.UI.ServerDriven.Validation

// ============================================================================
//  SseChannel (Phase 152, Track D — SSE+POST backend).
//
//  An `IFuaranLiveChannel` whose `Push` enqueues a `Frame` onto a BOUNDED
//  in-process queue; the GET-stream handler drains the queue and writes each
//  frame to the long-lived SSE response (`FrameWire.encodeSse`). The queue
//  decouples the writer (the POST request thread, which calls
//  `LiveConnection.Handle` → `channel.Push`) from the reader (the GET stream
//  thread that owns the response body) — the two are different requests, so a
//  direct cross-thread write to the response would race; the
//  `System.Threading.Channels` queue is the safe handoff.
//
//  ── Resource bound (Phase 212) ──────────────────────────────────────────────
//  The queue is bounded (`SseDefaults.FrameQueueCapacity`, overridable per
//  channel). A slow / stalled SSE reader that keeps POSTing events would
//  otherwise grow this queue without limit — one connection, unbounded server
//  memory. On overflow the channel CLOSES instead of growing: the drain loop
//  observes the completed queue and ends the stream, `EventSource`
//  auto-reconnects with `Last-Event-ID`, and `LiveConnection.Resync` replays
//  the missed frames — a clean reconnect-replay, not silent frame loss.
//
//  `Receive` stores the handler but the SSE backend never invokes it from here:
//  inbound events arrive on the POST endpoint and are routed to
//  `LiveConnection.Handle` via the connection registry, not through the channel.
// ============================================================================

/// Documented caps for the SSE backend (Phase 212).
[<RequireQualifiedAccess>]
module SseDefaults =
    /// Default bound of the per-connection SSE frame queue, in frames. A queue
    /// this deep means the reader is stalled, not slow — on overflow the
    /// channel closes and the client reconnect-replays via `Last-Event-ID`.
    [<Literal>]
    let FrameQueueCapacity = 256

/// An `IFuaranLiveChannel` backed by a bounded in-process frame queue, drained
/// by the SSE stream handler. `capacity` bounds the queue (default
/// `SseDefaults.FrameQueueCapacity`); a full queue closes the channel rather
/// than growing (the stalled-reader guard — see the module header).
type SseChannel(?capacity: int) =
    let frames =
        Channel.CreateBounded<Frame>(
            BoundedChannelOptions(
                defaultArg capacity SseDefaults.FrameQueueCapacity,
                FullMode = BoundedChannelFullMode.Wait
            )
        )

    let mutable handler: (LiveEvent -> unit) option = None
    let mutable closed = false

    let close () =
        closed <- true
        frames.Writer.TryComplete() |> ignore

    /// The queue reader the GET-stream handler drains.
    member _.Reader: ChannelReader<Frame> = frames.Reader

    /// True once `Close` completed the queue (host close or bounded-overflow
    /// close).
    member _.IsClosed = closed

    /// The inbound handler registered by `LiveConnection` (unused on the SSE
    /// path — inbound flows through the POST endpoint + registry).
    member _.Handler = handler

    interface IFuaranLiveChannel with
        member _.Push(frame) =
            // With `FullMode.Wait`, `TryWrite` returns false when the queue is
            // full (it never blocks). A full queue means the SSE reader is
            // stalled — close the connection instead of growing memory
            // unbounded (Phase 212); the client's reconnect-replay recovers.
            if not (frames.Writer.TryWrite frame) then
                close ()

        member _.Receive(h) = handler <- Some h

        member _.Close() = close ()
