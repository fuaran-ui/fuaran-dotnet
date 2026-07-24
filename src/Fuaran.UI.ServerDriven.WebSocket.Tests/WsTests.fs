module Fuaran.UI.ServerDriven.WebSocket.Tests.WsTests

// ─── Phase 152 Track D: WebSocket backend (Backend 2) ──────────────
//
// The testable halves: the shared Inbound parse (WS message JSON → LiveEvent —
// the SAME core `Fuaran.UI.ServerDriven.Inbound` the SSE+POST backend uses since
// Phase 211, so the two transports cannot diverge) and WsChannel (Push enqueues
// → Reader drains, the IFuaranLiveChannel impl the send pump reads). The WS
// accept + send/receive loops are thin ASP.NET glue, browser-verified.

open Expecto
open Fuaran.UI.ServerDriven
open Fuaran.UI.ServerDriven.Validation
open Fuaran.UI.ServerDriven.WebSocket

[<Tests>]
let inboundTests =
    testList
        "WS Inbound.parseLiveEvent"
        [ test "parses a WS message into a typed LiveEvent (connId server-assigned)" {
              let ev =
                  Inbound.parseLiveEvent "ws-1" """{"nodeId":"tab","event":"click","payload":{"index":2},"lastSeq":4}"""

              Expect.equal ev.ConnId "ws-1" "connId from the accept, not the body"
              Expect.equal ev.NodeId "tab" "nodeId"
              Expect.equal ev.Event "click" "event"
              Expect.equal ev.LastSeq 4 "lastSeq"
              Expect.equal (Map.find "index" ev.Payload) (LiveValue.Num 2.0) "numeric payload"
          }

          test "junk-but-valid degrades to safe empties (G1 rejects downstream)" {
              let ev = Inbound.parseLiveEvent "ws-1" """{"x":1}"""
              Expect.equal ev.NodeId "" "no nodeId"
              Expect.equal ev.Event "" "no event"
          }

          test "a malformed WS body is None via the shared guard — the recv loop skips it (Phase 211)" {
              Expect.isNone (Inbound.tryParseLiveEvent "ws-1" "}{ not json") "malformed → None (skip, no throw)"
          }

          test "an out-of-range lastSeq degrades to 0, never overflows (M1)" {
              let ev =
                  Inbound.parseLiveEvent "ws-1" """{"nodeId":"n","event":"click","lastSeq":9999999999999}"""

              Expect.equal ev.LastSeq 0 "out-of-range lastSeq → 0"
          } ]

[<Tests>]
let channelTests =
    testList
        "WsChannel"
        [ test "Push enqueues frames the Reader drains in order" {
              let channel = WsChannel()
              let iface = channel :> IFuaranLiveChannel

              let f1 =
                  { Seq = 1
                    Patches = [ DomPatch.SetText("a", "1") ]
                    Effects = [] }

              let f2 =
                  { Seq = 2
                    Patches = []
                    Effects = [ ClientEffect.Navigate "/x" ] }

              iface.Push f1
              iface.Push f2

              let drained = ResizeArray<Frame>()
              let mutable frame = Unchecked.defaultof<Frame>

              while channel.Reader.TryRead(&frame) do
                  drained.Add frame

              Expect.equal (List.ofSeq drained) [ f1; f2 ] "frames drained in push order"
          }

          test "Close completes the queue" {
              let channel = WsChannel()
              (channel :> IFuaranLiveChannel).Close()
              Expect.isTrue channel.IsClosed "closed"
              // A completed reader yields no more items.
              let mutable frame = Unchecked.defaultof<Frame>
              Expect.isFalse (channel.Reader.TryRead(&frame)) "no items after close"
          } ]

// ─── Phase 212 tail: bounded WS frame queue ─────────────────────────────────
//
// The per-connection WS frame queue is bounded (WsDefaults.FrameQueueCapacity,
// overridable per channel) — the same stalled-reader guard as the SSE backend's
// SseChannel. On overflow the channel CLOSES (the send pump observes the
// completed queue and ends; the client's WS adapter reconnects and Resync
// replays via Frame.Seq) instead of growing server memory without limit.

let private frame seq : Frame =
    { Seq = seq
      Patches = []
      Effects = [] }

[<Tests>]
let boundedQueueTests =
    testList
        "Phase 212 tail — bounded WS frame queue"
        [ test "pushes under the cap are queued and drainable" {
              let channel = WsChannel(capacity = 4)
              let live = channel :> IFuaranLiveChannel
              [ 1; 2; 3 ] |> List.iter (frame >> live.Push)

              Expect.isFalse channel.IsClosed "under the cap the channel stays open"

              let drained =
                  [ 1..3 ]
                  |> List.map (fun _ ->
                      match channel.Reader.TryRead() with
                      | true, f -> f.Seq
                      | false, _ -> failtest "expected a queued frame")

              Expect.equal drained [ 1; 2; 3 ] "frames drain in push order"
          }

          test "a full queue closes the channel instead of growing" {
              let channel = WsChannel(capacity = 2)
              let live = channel :> IFuaranLiveChannel
              live.Push(frame 1)
              live.Push(frame 2)
              Expect.isFalse channel.IsClosed "at capacity but not overflowed"

              live.Push(frame 3) // overflow — the stalled-reader guard
              Expect.isTrue channel.IsClosed "overflow closes the channel"

              // The queue is completed: the retained frames drain, then the
              // reader observes completion (the send pump ends).
              let mutable count = 0
              let mutable draining = true

              while draining do
                  match channel.Reader.TryRead() with
                  | true, _ -> count <- count + 1
                  | false, _ -> draining <- false

              Expect.equal count 2 "the overflowing frame was not enqueued"
              Expect.isTrue channel.Reader.Completion.IsCompleted "the queue is completed for the send pump"
          }

          testAsync "queue completion ends the send pump's wait (WaitToReadAsync yields false)" {
              // The pump loops `WaitToReadAsync` → drain → repeat, and exits on
              // a false wait (the queue completed). A completed queue must
              // yield false, not hang — this is the pump's exit arm.
              let channel = WsChannel(capacity = 2)
              (channel :> IFuaranLiveChannel).Close()
              let! more = channel.Reader.WaitToReadAsync().AsTask() |> Async.AwaitTask
              Expect.isFalse more "a completed queue unblocks the pump with no more frames"
          }

          test "a push after Close is a no-op (no throw, still closed)" {
              let channel = WsChannel(capacity = 2)
              let live = channel :> IFuaranLiveChannel
              live.Close()
              live.Push(frame 1)
              Expect.isTrue channel.IsClosed "closed stays closed"
          }

          test "the default capacity mirrors the SSE documented cap" {
              Expect.equal
                  WsDefaults.FrameQueueCapacity
                  256
                  "documented default (mirrors SseDefaults.FrameQueueCapacity)"
          } ]
