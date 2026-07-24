module Fuaran.UI.ServerDriven.AspNetCore.Tests.SseChannelTests

// ─── Phase 212: bounded SSE frame queue ─────────────────────────────────────
//
// The per-connection SSE frame queue is bounded (SseDefaults.FrameQueueCapacity,
// overridable per channel). A full queue means the reader is stalled, not slow —
// on overflow the channel CLOSES (the drain loop ends the stream; the client's
// EventSource reconnect-replays via Last-Event-ID) instead of growing server
// memory without limit.

open Expecto
open Fuaran.UI.ServerDriven
open Fuaran.UI.ServerDriven.AspNetCore

let private frame seq : Frame =
    { Seq = seq
      Patches = []
      Effects = [] }

[<Tests>]
let tests =
    testList
        "Phase 212 — bounded SSE frame queue"
        [ test "pushes under the cap are queued and drainable" {
              let channel = SseChannel(capacity = 4)
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

          test "a full queue closes the connection instead of growing" {
              let channel = SseChannel(capacity = 2)
              let live = channel :> IFuaranLiveChannel
              live.Push(frame 1)
              live.Push(frame 2)
              Expect.isFalse channel.IsClosed "at capacity but not overflowed"

              live.Push(frame 3) // overflow — the stalled-reader guard
              Expect.isTrue channel.IsClosed "overflow closes the channel"

              // The queue is completed: the retained frames drain, then the
              // reader observes completion (the drain loop ends the stream).
              let mutable count = 0
              let mutable draining = true

              while draining do
                  match channel.Reader.TryRead() with
                  | true, _ -> count <- count + 1
                  | false, _ -> draining <- false

              Expect.equal count 2 "the overflowing frame was not enqueued"
              Expect.isTrue channel.Reader.Completion.IsCompleted "the queue is completed for the drain loop"
          }

          test "a push after Close is a no-op (no throw, still closed)" {
              let channel = SseChannel(capacity = 2)
              let live = channel :> IFuaranLiveChannel
              live.Close()
              live.Push(frame 1)
              Expect.isTrue channel.IsClosed "closed stays closed"
          }

          test "the default capacity is the documented cap" {
              Expect.equal SseDefaults.FrameQueueCapacity 256 "documented default (SERVER_DRIVEN.md)"
          } ]
