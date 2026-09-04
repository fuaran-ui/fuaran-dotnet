module Fuaran.UI.ServerDriven.Tests.ChannelTests

// Phase 1152 — `Action.Dispatch` carries the IDL's `inProcessOnly` marking, which
// the generator renders as `[<Obsolete(…, false)>]`: FS0044 at every mention, and
// an error under this repo's `TreatWarningsAsErrors`. File-scoped rather than
// per-declaration because the mentions sit INSIDE `testList` expressions, where a
// lexical directive cannot be placed — this is the tightest form the file can
// express. A suite is not an authoring surface: these uses exist to PIN the marked
// case's behaviour, which is the one use the marking is not addressed to.
#nowarn "44"

// ─── Phase 152 Track D: the IFuaranLiveChannel seam + connection glue ──────
//
// The seam is what makes the SSE+POST / WebSocket backends drop-in swaps. The
// InMemoryChannel is the headlessly-testable reference: Send simulates an
// inbound client event, Pushed records server→client frames. LiveConnection
// wires a driver session to a channel — Send → step → Push.

open Expecto
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.ServerDriven
open Fuaran.UI.ServerDriven.Validation
open Fuaran.UI.ServerDriven.Driver

type Model = int

type Msg = Inc

let private update Inc (m: Model) : Model = m + 1

let private view (m: Model) : Node<Msg> =
    Fuaran.dashboard
        "root"
        { Defaults.dashboard<Msg> with
            Children =
                [ Fuaran.button
                      "inc"
                      { Defaults.button<Msg> with
                          OnClick = Action.Dispatch Inc }
                  Fuaran.markdown "count" (string m) ] }

let private stubRender (n: Node<Msg>) : string =
    let s = n.Id
    $"<f id='{s}'/>"

let private ev connId nodeId =
    { ConnId = connId
      NodeId = nodeId
      Event = "click"
      Payload = Map.empty
      LastSeq = 0 }

let private connect connId =
    let channel = InMemoryChannel()
    let session = init (DriverServices.createPermissive stubRender) update view 0
    let conn = LiveConnection(connId, session, channel)
    channel, conn

[<Tests>]
let tests =
    testList
        "Channel (IFuaranLiveChannel seam)"
        [ test "an inbound event drives the session and pushes one frame" {
              let channel, conn = connect "c1"
              channel.Send(ev "c1" "inc")

              Expect.equal conn.Session.Model 1 "session stepped"
              Expect.equal conn.Sequence 1 "op sequence advanced"

              match channel.Pushed with
              | [ frame ] ->
                  Expect.equal frame.Seq 1 "frame tagged with the sequence"

                  Expect.equal
                      frame.Patches
                      [ DomPatch.ReplaceFragment("count", "<f id='count'/>") ]
                      "frame carries the targeted patch"
              | other -> failtestf "expected exactly one frame, got %A" other
          }

          test "the sequence increments per pushed frame" {
              let channel, conn = connect "c1"
              channel.Send(ev "c1" "inc")
              channel.Send(ev "c1" "inc")
              channel.Send(ev "c1" "inc")

              Expect.equal conn.Session.Model 3 "three steps accumulated"
              Expect.equal conn.Sequence 3 "sequence advanced thrice"
              Expect.equal (List.length channel.Pushed) 3 "three frames pushed"
              Expect.equal (channel.Pushed |> List.map _.Seq) [ 1; 2; 3 ] "monotonic sequence"
          }

          test "events for a different connection id are ignored" {
              let channel, conn = connect "c1"
              channel.Send(ev "other" "inc")

              Expect.equal conn.Session.Model 0 "foreign-conn event did not step this session"
              Expect.isEmpty channel.Pushed "no frame pushed for a foreign connection"
          }

          test "a rejected event pushes no frame and does not advance the sequence" {
              let channel, conn = connect "c1"
              channel.Send(ev "c1" "ghost") // unknown node → G1 reject

              Expect.equal conn.Session.Model 0 "no state change"
              Expect.equal conn.Sequence 0 "sequence unchanged"
              Expect.isEmpty channel.Pushed "no frame on reject"
          }

          test "Resync re-pushes only the frames newer than the client's last seq" {
              let channel, conn = connect "c1"
              channel.Send(ev "c1" "inc") // seq 1
              channel.Send(ev "c1" "inc") // seq 2
              channel.Send(ev "c1" "inc") // seq 3

              // Client reconnects having applied up to seq 1 — it missed 2 and 3.
              let replayed = conn.Resync 1

              Expect.equal replayed 2 "two frames replayed"
              // Pushed now holds the 3 live frames + the 2 replayed (seq 2, 3).
              let replayedSeqs = channel.Pushed |> List.skip 3 |> List.map _.Seq
              Expect.equal replayedSeqs [ 2; 3 ] "only the missed frames, in order"
          }

          test "Resync past the latest seq replays nothing" {
              let channel, conn = connect "c1"
              channel.Send(ev "c1" "inc") // seq 1
              Expect.equal (conn.Resync 1) 0 "nothing newer than the client's seq"
          }

          test "Close marks the channel closed" {
              let channel, _ = connect "c1"
              (channel :> IFuaranLiveChannel).Close()
              Expect.isTrue channel.Closed "channel closed"
          }

          // ── Phase 212 — always-on reject sink ─────────────────────────────
          test "a G1 reject is recorded through OnReject with telemetry OFF" {
              let rejects = ResizeArray<RejectReason>()
              let channel = InMemoryChannel()

              let services =
                  { DriverServices.createPermissive stubRender with
                      OnReject = rejects.Add }

              // No EnableTelemetry call — the interaction-telemetry opt-in is
              // off (the default); the reject must still be recorded.
              let conn = LiveConnection("c1", init services update view 0, channel)
              channel.Send(ev "c1" "ghost") // unknown node → G1 reject

              Expect.equal conn.Session.Model 0 "no state change"
              Expect.isEmpty channel.Pushed "no frame on reject"

              match List.ofSeq rejects with
              | [ RejectReason.UnknownNode "ghost" ] -> ()
              | other -> failtestf "expected one UnknownNode reject, got %A" other

              Expect.isNotEmpty
                  (RejectReason.describe (Seq.head rejects))
                  "the recorded reason describes readably for the host log"
          }

          test "an accepted event does not fire OnReject" {
              let rejects = ResizeArray<RejectReason>()
              let channel = InMemoryChannel()

              let services =
                  { DriverServices.createPermissive stubRender with
                      OnReject = rejects.Add }

              let conn = LiveConnection("c1", init services update view 0, channel)
              channel.Send(ev "c1" "inc")

              Expect.equal conn.Session.Model 1 "session stepped"
              Expect.isEmpty rejects "no reject recorded for a legitimate event"
          }

          // ── Phase 212 — bounded reconnect-replay buffer ───────────────────
          test "the reconnect buffer is bounded: oldest frames evict at capacity" {
              let channel = InMemoryChannel()
              let session = init (DriverServices.createPermissive stubRender) update view 0

              let conn = LiveConnection("c1", session, channel, replayBufferCapacity = 2)

              channel.Send(ev "c1" "inc") // seq 1
              channel.Send(ev "c1" "inc") // seq 2
              channel.Send(ev "c1" "inc") // seq 3 — evicts seq 1

              // A full-history reconnect can only replay the retained window.
              let replayed = conn.Resync 0
              Expect.equal replayed 2 "only the retained (capped) frames replay"

              let replayedSeqs = channel.Pushed |> List.skip 3 |> List.map _.Seq
              Expect.equal replayedSeqs [ 2; 3 ] "the newest frames are retained, the oldest evicted"
          }

          test "the default reconnect-buffer cap retains recent frames as before" {
              let channel, conn = connect "c1"
              channel.Send(ev "c1" "inc") // seq 1
              channel.Send(ev "c1" "inc") // seq 2
              Expect.equal (conn.Resync 0) 2 "under the default cap nothing evicts"
          } ]
