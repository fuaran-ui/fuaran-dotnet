module Fuaran.UI.OpStream.Tests.CoreSnapshotTests

// ============================================================================
//  Phase 2305 — the linear checkpoint is Core's `Snapshot`, and replay from it
//  is Core's `Compacted` fold.
//
//  Three claims, each shown with a go-red half:
//
//   1. A checkpoint's hash reproduces from Core's seal: `Checkpoint.create`
//      mints exactly the hash `Snapshots.take` mints over the same stream mapped
//      onto Core's records, and its pre-image is Core's strict
//      `{"snapshot":true,"seq":N,"state":…}`.
//   2. Both durable stores' checkpoints, after a real compaction, verify under
//      Core's `Snapshots.verify` and replay under `Compacted.replayFrom` to the
//      state a replay from genesis reached — the store-level instantiation of
//      the snapshot claim `Conformance.snapshotLaws` certifies over the witness
//      (`PersistenceLawsTests.fs`).
//   3. A break ANYWHERE in the tail is refused by name before anything is
//      applied — in the middle of the tail, not only at its first link.
// ============================================================================

open System
open System.IO
open Expecto
open Microsoft.Data.Sqlite
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Ops.Types
open Fuaran.UI.OpStream.Abstractions
open Fuaran.UI.OpStream.InMemory
open Fuaran.UI.OpStream.Sqlite
open Fuaran.UI.OpStream.Replay
open Fuaran.UI.OpStream.TestSupport

/// Five ops that all apply in order over `buildDashboard ()`.
let private ops: TreeOp<TestMsg> list =
    [ TreeOp.InsertChild(NodeId "dash", Fuaran.markdown "a" "A")
      TreeOp.InsertChild(NodeId "dash", Fuaran.markdown "b" "B")
      TreeOp.RemoveNode(NodeId "right")
      TreeOp.InsertChild(NodeId "dash", Fuaran.markdown "c" "C")
      TreeOp.RemoveNode(NodeId "a") ]

/// The chained records of `ops` on `streamId`, sequences 1..5.
let private chain (streamId: string) : OpRecord<TestMsg> list =
    ops
    |> List.indexed
    |> List.fold
        (fun (acc: OpRecord<TestMsg> list) (i, op) ->
            acc
            @ [ buildRecord streamId (i + 1) op (List.tryLast acc) (timestamp (1_000L + int64 i)) ])
        []

/// The tree after the first `n` ops.
let private treeAfter (n: int) : Node<TestMsg> =
    ops
    |> List.truncate n
    |> List.fold (fun t op -> applyOk op t) (buildDashboard ())

/// The tier's linear stream as a Core `Stream` — the canonical config with the
/// tier's 64-zero genesis, SHA-256, and the provenance envelope as the op.
let private coreStream =
    Fuaran.Core.OpStream.ofConfig HashChain.chainConfig StreamEntry.hashFn (StreamEntry.coreWitness<TestMsg> ())

let private appendAll (sink: IOpStreamCheckpointSink<TestMsg>) (records: OpRecord<TestMsg> list) =
    for r in records do
        sink.Append r |> Async.RunSynchronously

/// Run `body` with the path of a fresh SQLite file in a test temp directory
/// (`TempDir`, which deletes it); the pools are cleared first so the file is
/// released.
let private withDb (body: string -> unit) =
    use tmp = Fuaran.UI.Testing.TempDir.create "core-snapshot"

    try
        body (Path.Combine(tmp.Path, "store.db"))
    finally
        SqliteConnection.ClearAllPools()

/// Run `body` over both durable stores (the SQLite one with the reference node
/// codec, so a snapshot reads back).
let private overBothStores (body: string -> IOpStreamCheckpointSink<TestMsg> -> unit) =
    body "InMemory" (InMemorySink.createWithCheckpoints<TestMsg> ())

    withDb (fun path ->
        body "Sqlite" (SqliteSink.createWithCheckpoints (sprintf "Data Source=%s" path) testCodec testNodeCodec))

/// The pre-2305 seal, restated here as a FROZEN vector of the old format (the
/// tier computes it nowhere now): `"tree"` where Core has `"state"`.
let private legacySeal (head: string) (sequence: int) (tree: Node<TestMsg>) : string =
    Fuaran.Core.OpStream.sha256Hash
        head
        ("{\"snapshot\":true,\"seq\":"
         + string sequence
         + ",\"tree\":"
         + canonical tree
         + "}")

[<Tests>]
let tests =
    testList
        "Fuaran.UI.OpStream — the checkpoint is Core's Snapshot (Phase 2305)"
        [

          test "Checkpoint.create mints Core's seal: Snapshots.take over the mapped stream gives the same hash" {
              let sink = InMemorySink.createWithCheckpoints<TestMsg> ()
              let records = chain "seal"
              appendAll sink (List.truncate 3 records)
              let cp = Checkpoint.create sink "seal" (treeAfter 3) |> Async.RunSynchronously

              match
                  Fuaran.Core.Snapshots.take
                      Fuaran.Core.SnapshotMode.Strict
                      coreStream
                      CanonicalJson.encodeNode
                      (buildDashboard ())
                      (records |> List.map StreamEntry.toCoreRecord)
                      3
              with
              | Error e -> failtestf "Core refused to take the snapshot: %A" e
              | Ok snap ->
                  Expect.equal cp.SnapshotHash snap.Hash "the UI checkpoint's hash IS Core's snapshot hash"
                  Expect.equal cp.PreviousChainHead snap.PrevHash "the boundary hash is Core's"
                  Expect.equal cp.Sequence snap.Seq "the boundary sequence is Core's"

              // The pre-image, stated: Core's strict shape under "state".
              let expected =
                  Fuaran.Core.OpStream.sha256Hash
                      cp.PreviousChainHead
                      ("{\"snapshot\":true,\"seq\":3,\"state\":" + canonical cp.Snapshot + "}")

              Expect.equal cp.SnapshotHash expected "the seal's pre-image carries \"state\""

              // Go-red: the pre-2305 "tree" seal is a DIFFERENT hash, so a store
              // sealed under it does not verify.
              Expect.notEqual
                  cp.SnapshotHash
                  (legacySeal cp.PreviousChainHead 3 cp.Snapshot)
                  "the old seal must not coincide with Core's"
          }

          test "both durable stores' compacted checkpoints verify and replay under Core's Snapshots / Compacted" {
              overBothStores (fun storeName sink ->
                  let streamId = "certify-" + storeName
                  let records = chain streamId
                  appendAll sink (List.truncate 1 records)

                  Checkpoint.create sink streamId (treeAfter 1)
                  |> Async.RunSynchronously
                  |> ignore

                  appendAll sink (records |> List.skip 1 |> List.truncate 2)

                  Checkpoint.create sink streamId (treeAfter 3)
                  |> Async.RunSynchronously
                  |> ignore

                  appendAll sink (List.skip 3 records)

                  let truncated =
                      Compaction.applyPolicy sink streamId (CompactionPolicy.keep 1)
                      |> Async.RunSynchronously

                  Expect.equal truncated 3 (storeName + ": ops 1..3 are compacted into the checkpoint at 3")

                  let cp =
                      match sink.LatestCheckpointAtOrBefore(streamId, 4) |> Async.RunSynchronously with
                      | Some cp -> cp
                      | None -> failtestf "%s: no checkpoint at or before 4" storeName

                  let tail = sink.Replay(streamId, cp.Sequence + 1, 5) |> Async.RunSynchronously
                  let coreTail = tail |> List.map StreamEntry.toCoreRecord
                  let snap = CheckpointSeal.toSnapshot cp

                  Expect.isTrue
                      (Fuaran.Core.Snapshots.verify coreStream CanonicalJson.encodeNode snap coreTail)
                      (storeName + ": Core's Snapshots.verify accepts the store's checkpoint and tail")

                  let compacted: Fuaran.Core.Compacted<StreamEntry<TestMsg>, Node<TestMsg>> =
                      { Snapshot = snap
                        Tail = coreTail
                        Keys = Fuaran.Core.KeyIndex.empty }

                  match Fuaran.Core.Compacted.replayFrom coreStream.Witness compacted with
                  | Ok tree ->
                      Expect.equal
                          (canonical tree)
                          (canonical (treeAfter 5))
                          (storeName + ": Core's fold reaches the head")
                  | Error e -> failtestf "%s: Core's fold refused the store's checkpoint: %A" storeName e

                  match
                      CheckpointedReplay.applyFromCheckpoint sink (buildDashboard ()) streamId 5
                      |> Async.RunSynchronously
                  with
                  | Ok tree ->
                      Expect.equal
                          (canonical tree)
                          (canonical (treeAfter 5))
                          (storeName + ": the tier's resume agrees")
                  | Error e -> failtestf "%s: applyFromCheckpoint refused: %A" storeName e

                  // Go-red: the same checkpoint with its state swapped fails Core's verify.
                  Expect.isFalse
                      (Fuaran.Core.Snapshots.verify
                          coreStream
                          CanonicalJson.encodeNode
                          { snap with State = treeAfter 2 }
                          coreTail)
                      (storeName + ": a swapped state fails the strict seal"))
          }

          test "a break in the MIDDLE of the tail is refused by name, before anything is applied" {
              // The store neither checks writes nor verifies reads, so the corrupt
              // records reach the checkpoint path itself — the path under test.
              let sink =
                  InMemorySink.createWithCheckpointsAndModes<TestMsg> LoadVerification.Off WriteAdmission.Off

              let records = chain "mid"
              appendAll sink (List.truncate 2 records)
              let cp = Checkpoint.create sink "mid" (treeAfter 2) |> Async.RunSynchronously

              // Record 4 — the SECOND record of the tail (3..5) — no longer
              // recomputes; its first link (3) is intact.
              let r4 = records[3]

              let corrupt =
                  { r4 with
                      Timestamp = r4.Timestamp.AddSeconds 1.0 }

              appendAll sink [ records[2]; corrupt; records[4] ]

              match
                  Fuaran.Core.Sealed.firstBreak
                      StreamEntry.hashFn
                      CanonicalJson.encodeNode
                      (HashChain.recordShape ())
                      (Fuaran.Core.Sealed.ofSnapshot (CheckpointSeal.toSnapshot cp))
                      (sink.Replay("mid", 3, 5) |> Async.RunSynchronously)
              with
              | Some(Fuaran.Core.SnapshotBreak.Tail b) ->
                  Expect.equal b.Index 1 "Core's walker finds the break at the tail's second record"
              | other -> failtestf "expected a tail break at index 1, got %A" other

              match
                  CheckpointedReplay.applyFromCheckpoint sink (buildDashboard ()) "mid" 5
                  |> Async.RunSynchronously
              with
              | Error(CheckpointReplayError.TailBroken(cpSeq, VerificationError.HashMismatch(sequence, _, _))) ->
                  Expect.equal cpSeq 2 "names the checkpoint"
                  Expect.equal sequence 4 "names the first bad record"
              | other -> failtestf "GO-RED FAILED: expected TailBroken/HashMismatch at 4, got %A" other
          }

          test "a sequence gap in the middle of the tail is refused by name" {
              let sink =
                  InMemorySink.createWithCheckpointsAndModes<TestMsg> LoadVerification.Off WriteAdmission.Off

              let records = chain "gap"
              appendAll sink (List.truncate 2 records)
              Checkpoint.create sink "gap" (treeAfter 2) |> Async.RunSynchronously |> ignore
              // Record 4 is missing: the tail reads [3; 5].
              appendAll sink [ records[2]; records[4] ]

              match
                  CheckpointedReplay.applyFromCheckpoint sink (buildDashboard ()) "gap" 5
                  |> Async.RunSynchronously
              with
              | Error(CheckpointReplayError.TailBroken(2, VerificationError.OutOfOrder(expected, actual))) ->
                  Expect.equal (expected, actual) (4, 5) "names the missing sequence and the one found"
              | other -> failtestf "GO-RED FAILED: expected TailBroken/OutOfOrder(4, 5), got %A" other
          } ]
