module Fuaran.UI.ServerDriven.Tests.ReattachTests

#nowarn "44" // `Action.Dispatch` is in-process-only (Phase 1152); this suite pins its behaviour in-process.

// ─── Phase 1928: the transport-agnostic half of a reconnect ────────────────
//
// `LiveConnection.Reattach` catches a returning client up under the
// connection's gate: a replay when every frame it missed is still retained,
// otherwise one full-document re-baseline — never a partial replay, which would
// patch a DOM the server no longer describes. And `Handle` / `Resync` are
// serialised, so overlapping events cannot lose a transition or share a `Seq`.

open System.Threading.Tasks
open Expecto
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.ServerDriven
open Fuaran.UI.ServerDriven.Validation
open Fuaran.UI.ServerDriven.Driver

type Msg = Inc

let private view (m: int) : Node<Msg> =
    Fuaran.dashboard
        "root"
        { Defaults.dashboard<Msg> with
            Children =
                [ Fuaran.button
                      "inc"
                      { Defaults.button<Msg> with
                          OnClick = Action.Dispatch Inc }
                  Fuaran.markdown "count" (string m) ] }

let private render (n: Node<Msg>) = $"<f id='{n.Id}'/>"

let private click =
    { ConnId = "c"
      NodeId = "inc"
      Event = "click"
      Payload = Map.empty
      LastSeq = 0 }

let private connect (capacity: int) (update: Msg -> int -> int) =
    let channel = InMemoryChannel()
    let session = init (DriverServices.createPermissive render) update view 0
    channel, LiveConnection("c", session, channel, capacity)

let private isRebaseline (frame: Frame) =
    match frame.Patches with
    | [ DomPatch.ReplaceFragment("root", _) ] -> true
    | _ -> false

[<Tests>]
let tests =
    testList
        "Phase 1928 — LiveConnection.Reattach"
        [ test "inside the retained window, the missed frames are replayed after attach runs" {
              let channel, conn = connect 8 (fun Inc m -> m + 1)

              for _ in 1..3 do
                  conn.Handle click

              let mutable pushedWhenAttached = -1

              let outcome =
                  conn.Reattach(1, (fun () -> pushedWhenAttached <- channel.Pushed.Length))

              Expect.equal outcome (ResyncOutcome.Replayed 2) "frames 2 and 3 replayed"
              Expect.equal pushedWhenAttached 3 "attach ran before anything was re-pushed"
              Expect.equal (channel.Pushed |> List.skip 3 |> List.map _.Seq) [ 2; 3 ] "in order, once each"
          }

          test "a client with nothing missed replays nothing" {
              let channel, conn = connect 8 (fun Inc m -> m + 1)
              conn.Handle click
              Expect.equal (conn.Reattach(1, ignore)) (ResyncOutcome.Replayed 0) "nothing to replay"
              Expect.equal channel.Pushed.Length 1 "nothing re-pushed"
          }

          test "behind the retained window, one full-document frame re-baselines the current session" {
              let channel, conn = connect 2 (fun Inc m -> m + 1)

              for _ in 1..5 do
                  conn.Handle click

              Expect.equal (conn.Reattach(1, ignore)) ResyncOutcome.Rebaselined "frames 2 and 3 were evicted"
              let last = List.last channel.Pushed
              Expect.isTrue (isRebaseline last) "the frame replaces the whole document"
              Expect.equal last.Seq 6 "under the next sequence number"
              Expect.equal conn.Session.Model 5 "the session is the one the client had: nothing reset"
              Expect.equal channel.Pushed.Length 6 "no partial replay before it"
          }

          test "a sequence this connection never pushed is re-baselined" {
              let channel, conn = connect 8 (fun Inc m -> m + 1)
              conn.Handle click
              Expect.equal (conn.Reattach(7, ignore)) ResyncOutcome.Rebaselined "a client ahead of the connection"
              Expect.equal (conn.Reattach(-1, ignore)) ResyncOutcome.Rebaselined "a negative sequence"
              Expect.isTrue (channel.Pushed |> List.skip 1 |> List.forall isRebaseline) "only re-baselines pushed"
          }

          testTask "overlapping Handle calls are serialised: distinct Seq values, no lost transition" {
              let slow Inc m =
                  System.Threading.Thread.Sleep 20
                  m + 1

              let channel, conn = connect 64 slow
              // Awaited, not waited on: the test holds no pool thread the steps need.
              let steps = [| for _ in 1..8 -> Task.Run(fun () -> conn.Handle click) |]
              do! Task.WhenAll steps
              Expect.equal conn.Session.Model 8 "every transition applied"
              Expect.equal (channel.Pushed |> List.map _.Seq |> List.sort) [ 1..8 ] "one Seq per frame"
          }

          test "Rebaseline pushes the current tree under the next sequence" {
              let channel, conn = connect 8 (fun Inc m -> m + 1)
              conn.Rebaseline()
              Expect.equal channel.Pushed.Length 1 "one frame"
              Expect.isTrue (isRebaseline channel.Pushed.Head) "of the whole document"
              Expect.equal conn.Sequence 1 "sequenced like any frame"
          } ]
