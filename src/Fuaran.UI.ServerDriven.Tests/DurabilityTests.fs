module Fuaran.UI.ServerDriven.Tests.DurabilityTests

// Phase 1152 — `Action.Dispatch` carries the IDL's `inProcessOnly` marking, which
// the generator renders as `[<Obsolete(…, false)>]`: FS0044 at every mention, and
// an error under this repo's `TreatWarningsAsErrors`. File-scoped rather than
// per-declaration because the mentions sit INSIDE `testList` expressions, where a
// lexical directive cannot be placed — this is the tightest form the file can
// express. A suite is not an authoring surface: these uses exist to PIN the marked
// case's behaviour, which is the one use the marking is not addressed to.
#nowarn "44"

// ─── Phase 155 (Wave 18): server-driven session durability ────────────────────
//
// Checkpoint + hash-chained journal = session reconstruction, behind the
// `IFuaranSessionStore` seam. These tests prove the four acceptance criteria:
//   AC1 — a session survives a server restart (reconstruct from checkpoint + tail).
//   AC2 — a session resumes on a DIFFERENT node via a shared store.
//   AC3 — journal integrity is verified BEFORE replay (tamper → explicit error).
//   AC4 — the in-memory default preserves current 152 behaviour (no driver API
//         change; enabling durability does not alter the pushed frames).
//
// The store is exercised directly (driver-agnostic) for reconstruction, plus
// through `LiveConnection.EnableDurability` for the journal/checkpoint wiring.

open System
open Expecto
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Ops.Types
open Fuaran.UI.OpStream.Abstractions
open Fuaran.UI.OpStream.Replay
open Fuaran.UI.ServerDriven
open Fuaran.UI.ServerDriven.Validation
open Fuaran.UI.ServerDriven.Driver

// ── a minimal closure-driver app: a click increments a rendered count ──────────

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

// A fixed clock so the hash chain (which folds the timestamp) is deterministic.
let private fixedClock () : DateTimeOffset =
    DateTimeOffset(2026, 6, 8, 0, 0, 0, TimeSpan.Zero)

let private ev connId nodeId : LiveEvent =
    { ConnId = connId
      NodeId = nodeId
      Event = "click"
      Payload = Map.empty
      LastSeq = 0 }

let private canon (n: Node<Msg>) : string = CanonicalJson.encodeNode n

// A real op off the diff engine — used to forge journal records in the AC3 tests.
let private anOp: TreeOp<Msg> = TreeOpDiff.diff (view 0) (view 1) |> List.head

[<Tests>]
let tests =
    testList
        "Session durability (Phase 155)"
        [ test "AC1 — a session survives a server restart" {
              // Node "before restart": drive three clicks through a durable
              // connection that checkpoints every two ops.
              let store = SessionStore.inMemory<Msg> ()
              let channel = InMemoryChannel()

              let conn =
                  LiveConnection("c1", init (DriverServices.createPermissive stubRender) update view 0, channel)

              conn.EnableDurability(store, checkpointEvery = 2, clock = fixedClock)

              channel.Send(ev "c1" "inc")
              channel.Send(ev "c1" "inc")
              channel.Send(ev "c1" "inc")

              Expect.equal conn.Session.Model 3 "three steps accumulated before the crash"
              let headSeq = conn.DurableSequence

              // "Restart": the in-memory session (conn/channel) is gone; only the
              // store survives. Reconstruct from it.
              match SessionReplay.reconstruct store "c1" (view 0) |> Async.RunSynchronously with
              | Ok(tree, seq, _head) ->
                  Expect.equal (canon tree) (canon (view 3)) "reconstructed tree matches the pre-crash state"
                  Expect.equal seq headSeq "reconstruction reaches the journal head — nothing lost"
              | Error e -> failtestf "reconstruct failed: %A" e
          }

          test "AC1 — reconstruction with no checkpoint replays the whole journal from genesis" {
              let store = SessionStore.inMemory<Msg> ()
              let channel = InMemoryChannel()

              let conn =
                  LiveConnection("c2", init (DriverServices.createPermissive stubRender) update view 0, channel)
              // checkpointEvery defaults to 0 → never auto-checkpoints; the journal
              // alone must carry the full state.
              conn.EnableDurability(store, clock = fixedClock)

              channel.Send(ev "c2" "inc")
              channel.Send(ev "c2" "inc")

              match SessionReplay.reconstruct store "c2" (view 0) |> Async.RunSynchronously with
              | Ok(tree, _, _) -> Expect.equal (canon tree) (canon (view 2)) "genesis replay reconstructs the live tree"
              | Error e -> failtestf "reconstruct failed: %A" e
          }

          test "AC2 — a session resumes on a different node via a shared store" {
              // One store instance models the shared backend both nodes reach.
              let shared = SessionStore.inMemory<Msg> ()

              // Node A drives the session, then checkpoints on graceful disconnect.
              let chA = InMemoryChannel()

              let connA =
                  LiveConnection("s1", init (DriverServices.createPermissive stubRender) update view 0, chA)

              connA.EnableDurability(shared, checkpointEvery = 1, clock = fixedClock)
              chA.Send(ev "s1" "inc")
              chA.Send(ev "s1" "inc")
              connA.CheckpointNow()

              // Node B: a brand-new connection on a DIFFERENT channel, same shared
              // store, with no in-memory knowledge of the session.
              let chB = InMemoryChannel()

              let connB =
                  LiveConnection("s1", init (DriverServices.createPermissive stubRender) update view 0, chB)

              connB.EnableDurability(shared, clock = fixedClock)

              match SessionReplay.reconstruct shared "s1" (view 0) |> Async.RunSynchronously with
              | Ok(tree, seq, head) ->
                  Expect.equal (canon tree) (canon (view 2)) "node B reconstructs node A's exact state"
                  connB.ResumeFrom(tree, seq, head)

                  // Node B pushed a single full-document resync frame to its client.
                  match chB.Pushed with
                  | [ frame ] ->
                      match frame.Patches with
                      | [ DomPatch.ReplaceFragment("root", _) ] -> ()
                      | other -> failtestf "expected a full-document root resync, got %A" other
                  | other -> failtestf "expected exactly one resync frame on node B, got %A" other

                  // Node B's journal continues the SAME chain from the resumed head.
                  Expect.equal connB.DurableSequence seq "node B picked up node A's journal head"
              | Error e -> failtestf "reconstruct on node B failed: %A" e
          }

          test "AC3 — a tampered checkpoint snapshot surfaces an explicit error" {
              let store = SessionStore.inMemory<Msg> ()

              // A checkpoint whose stored SnapshotHash does not match its snapshot.
              let badCheckpoint: Checkpoint<Msg> =
                  { StreamId = "t1"
                    Sequence = 1
                    PreviousChainHead = HashChain.genesisPreviousHash
                    SnapshotHash = "deadbeef"
                    Snapshot = view 0
                    Timestamp = fixedClock () }

              store.Checkpoint("t1", badCheckpoint) |> Async.RunSynchronously

              match SessionReplay.reconstruct store "t1" (view 0) |> Async.RunSynchronously with
              | Error(SessionReconstructError.SnapshotHashMismatch _) -> ()
              | other -> failtestf "expected SnapshotHashMismatch, got %A" other
          }

          test "AC3 — a forked / tampered journal segment surfaces an explicit error" {
              let store = SessionStore.inMemory<Msg> ()
              let ts = fixedClock ()

              // A correct first record, then a second whose Hash has been tampered.
              let hash1 =
                  HashChain.computeHash
                      HashChain.genesisPreviousHash
                      anOp
                      1
                      ts
                      (Actor.Human "u")
                      None
                      OpResultEnvelope.Success

              let rec1: OpRecord<Msg> =
                  { StreamId = "t2"
                    Sequence = 1
                    PreviousHash = HashChain.genesisPreviousHash
                    Hash = hash1
                    Op = anOp
                    PromptId = None
                    Actor = Actor.Human "u"
                    Timestamp = ts
                    ResultEnvelope = OpResultEnvelope.Success }

              let rec2Tampered: OpRecord<Msg> =
                  { StreamId = "t2"
                    Sequence = 2
                    PreviousHash = hash1
                    Hash = "0000000000000000000000000000000000000000000000000000000000000000"
                    Op = anOp
                    PromptId = None
                    Actor = Actor.Human "u"
                    Timestamp = ts
                    ResultEnvelope = OpResultEnvelope.Success }

              store.AppendOp("t2", rec1) |> Async.RunSynchronously
              store.AppendOp("t2", rec2Tampered) |> Async.RunSynchronously

              match SessionReplay.reconstruct store "t2" (view 0) |> Async.RunSynchronously with
              | Error(SessionReconstructError.JournalIntegrity(VerificationError.HashMismatch _)) -> ()
              | other -> failtestf "expected JournalIntegrity/HashMismatch, got %A" other
          }

          test "AC3 — a gap in the journal sequence is rejected before replay" {
              let store = SessionStore.inMemory<Msg> ()
              let ts = fixedClock ()
              // A lone record at Sequence 2 — no record 1. Verification expects a
              // contiguous chain from genesis.
              let orphan: OpRecord<Msg> =
                  { StreamId = "t3"
                    Sequence = 2
                    PreviousHash = HashChain.genesisPreviousHash
                    Hash =
                      HashChain.computeHash
                          HashChain.genesisPreviousHash
                          anOp
                          2
                          ts
                          (Actor.Human "u")
                          None
                          OpResultEnvelope.Success
                    Op = anOp
                    PromptId = None
                    Actor = Actor.Human "u"
                    Timestamp = ts
                    ResultEnvelope = OpResultEnvelope.Success }

              store.AppendOp("t3", orphan) |> Async.RunSynchronously

              match SessionReplay.reconstruct store "t3" (view 0) |> Async.RunSynchronously with
              | Error(SessionReconstructError.JournalIntegrity(VerificationError.OutOfOrder _)) -> ()
              | other -> failtestf "expected JournalIntegrity/OutOfOrder, got %A" other
          }

          test "AC4 — enabling durability does not change the pushed frames; the host OnApply still fires" {
              let mutable hostOps = 0

              let services =
                  { DriverServices.createPermissive stubRender with
                      OnApply = fun ops -> hostOps <- hostOps + List.length ops }

              let store = SessionStore.inMemory<Msg> ()
              let channel = InMemoryChannel()
              let conn = LiveConnection("d1", init services update view 0, channel)
              conn.EnableDurability(store, clock = fixedClock)

              channel.Send(ev "d1" "inc")
              channel.Send(ev "d1" "inc")

              Expect.equal conn.Session.Model 2 "session stepped under durability"
              Expect.isGreaterThan hostOps 0 "the host's own OnApply sink still fires (FGP 5 preserved)"
              Expect.equal (List.length channel.Pushed) 2 "exactly the same two frames the non-durable path pushes"

              // The journal captured those ops — reconstruct round-trips the tree.
              match SessionReplay.reconstruct store "d1" (view 0) |> Async.RunSynchronously with
              | Ok(tree, _, _) -> Expect.equal (canon tree) (canon (view 2)) "journal reconstructs the live tree"
              | Error e -> failtestf "reconstruct failed: %A" e
          }

          test "AC4 — a connection with durability OFF behaves exactly as the 152 path" {
              // No EnableDurability call — the ctor + Handle path is untouched.
              let channel = InMemoryChannel()

              let conn =
                  LiveConnection("p1", init (DriverServices.createPermissive stubRender) update view 0, channel)

              channel.Send(ev "p1" "inc")
              channel.Send(ev "p1" "inc")

              Expect.equal conn.Session.Model 2 "stepped"
              Expect.equal conn.Sequence 2 "frame sequence advanced exactly as before"
              Expect.equal (List.length channel.Pushed) 2 "two frames, no durability side effects"
          }

          test "session lifecycle — idle sessions are garbage-collected, live ones retained" {
              let registry = SessionRegistry()
              let t0 = fixedClock ()
              registry.Touch("old", t0)
              registry.Touch("fresh", t0.AddMinutes 9.0)

              let now = t0.AddMinutes 10.0
              let collected = registry.GarbageCollect(TimeSpan.FromMinutes 5.0, now)

              Expect.equal collected [ "old" ] "the session idle past the threshold is collected"
              Expect.equal registry.Active [ "fresh" ] "the recently-active session is retained"

              registry.End "fresh"
              Expect.isEmpty registry.Active "explicit session-end drops the session"
          } ]
