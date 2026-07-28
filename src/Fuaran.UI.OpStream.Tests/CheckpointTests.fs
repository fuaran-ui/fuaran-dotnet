module Fuaran.UI.OpStream.Tests.CheckpointTests

open System
open System.IO
open Expecto
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Ops
open Fuaran.UI.Ops.Types
open Fuaran.UI.OpStream.Abstractions
open Fuaran.UI.OpStream.InMemory
open Fuaran.UI.OpStream.Sqlite
open Fuaran.UI.OpStream.Replay
open Fuaran.UI.OpStream.Tests.TestSupport

// ============================================================================
//  Checkpoint + compaction acceptance.
//
//  Pinned properties:
//   1. Equivalence — Replay-from-checkpoint produces the same tree as
//      Replay-from-genesis (apply-engine equivalence under checkpointing).
//   2. Snapshot integrity — tampering with `Checkpoint.Snapshot` flips
//      `verifySnapshotHash` to Error and surfaces a
//      `SnapshotHashMismatch` via the replay-from-checkpoint path.
//   3. Boundary integrity — a tail whose first record's `PreviousHash`
//      doesn't match the retained checkpoint's `PreviousChainHead`
//      surfaces a `BoundaryMismatch`.
//   4. Compaction — `Compaction.applyPolicy` retains the last K
//      checkpoints and truncates ops with sequence ≤ oldest-retained.
//      Surviving tail remains chain-verifiable on its own.
//   5. Retention disabled — `KeepCheckpoints ≤ 0` is a no-op.
//   6. Sqlite — Append + List + Truncate round-trip the new
//      `op_checkpoint` table; read-back of the snapshot is exercised
//      against the InMemory sink (Sqlite read-back requires a real
//      node-decoder, which is host territory).
// ============================================================================

let private childIds (root: Node<TestMsg>) : string list =
    match root.Kind with
    | NodeKind.Box(spec) ->
        spec.Children
        |> List.map (fun n ->
            match n.Id with
            | NodeId raw -> raw)
    | _ -> failwithf "Expected dashboard, got %A" root.Kind

/// Build a small op sequence + a sink populated with its records. Returns
/// the final tree (the genesis-replay result) so tests can compare
/// checkpoint-resumed trees against it.
let private populate (streamId: string) (sink: IOpStreamCheckpointSink<TestMsg>) : Node<TestMsg> =
    let tree = buildDashboard ()

    let ops: TreeOp<TestMsg> list =
        [ TreeOp.RemoveNode(NodeId "right")
          TreeOp.InsertChild(NodeId "dash", Fuaran.markdown "middle" "Middle pane")
          TreeOp.RemoveNode(NodeId "left")
          TreeOp.InsertChild(NodeId "dash", Fuaran.markdown "header" "Header")
          TreeOp.InsertChild(NodeId "dash", Fuaran.markdown "footer" "Footer") ]

    let mutable previous: OpRecord<TestMsg> option = None
    let mutable seq = 1

    for op in ops do
        let ts = timestamp (int64 (100 * seq))
        let record = buildRecord streamId seq op previous ts
        sink.Append record |> Async.RunSynchronously
        previous <- Some record
        seq <- seq + 1

    match Replay.applyTo tree (sink.Replay(streamId, 1, 1000) |> Async.RunSynchronously) with
    | Ok t -> t
    | Error e -> failtestf "Genesis replay failed: %A" e

[<Tests>]
let tests =
    testList
        "Fuaran.UI.OpStream — checkpoint + compaction"
        [ test "Empty stream — applyFromCheckpoint with no checkpoint returns initial tree" {
              let sink = InMemorySink.createWithCheckpoints<TestMsg> ()
              let initial = buildDashboard ()

              let result =
                  CheckpointedReplay.applyFromCheckpoint sink initial "empty" 0
                  |> Async.RunSynchronously

              match result with
              | Ok tree -> Expect.equal (childIds tree) (childIds initial) "Initial tree returned unchanged"
              | Error e -> failtestf "Expected Ok, got %A" e
          }

          test "Equivalence — replay-from-checkpoint matches replay-from-genesis" {
              let sink = InMemorySink.createWithCheckpoints<TestMsg> ()
              let genesisResult = populate "eq" sink

              // Materialise the mid-stream tree (state after applying ops 1..3)
              // and hand-construct a checkpoint at sequence 3 so the test pins
              // the (sequence, snapshot) pair precisely (Checkpoint.create uses
              // `sink.LatestSequence` which is 5 by this point — not what we
              // want for the equivalence assertion below).
              let midTreeFromGenesis =
                  match Replay.applyTo (buildDashboard ()) (sink.Replay("eq", 1, 3) |> Async.RunSynchronously) with
                  | Ok t -> t
                  | Error e -> failtestf "Mid-stream genesis replay failed: %A" e

              let prevAt3 =
                  match sink.Replay("eq", 3, 3) |> Async.RunSynchronously with
                  | r :: _ -> r
                  | [] -> failtest "Expected record at sequence 3"

              let canonical = CanonicalJson.encodeNode midTreeFromGenesis
              let snapshotHash = HashChain.snapshotHash prevAt3.Hash 3 canonical

              let cpAt3: Checkpoint<TestMsg> =
                  { StreamId = "eq"
                    Sequence = 3
                    PreviousChainHead = prevAt3.Hash
                    SnapshotHash = snapshotHash
                    Snapshot = midTreeFromGenesis
                    Timestamp = DateTimeOffset.UtcNow }

              sink.AppendCheckpoint cpAt3 |> Async.RunSynchronously

              // Resolve nearest checkpoint ≤ 5 → cpAt3 + tail [4, 5]. Should
              // produce a tree equal to genesisResult.
              let cpResult =
                  CheckpointedReplay.applyFromCheckpoint sink (buildDashboard ()) "eq" 5
                  |> Async.RunSynchronously

              match cpResult with
              | Ok tree ->
                  Expect.equal
                      (childIds tree)
                      (childIds genesisResult)
                      "Checkpoint-resumed tree equals genesis-replay tree"
              | Error e -> failtestf "Expected Ok, got %A" e

              // Resolve at target 3 directly (snapshot IS the answer; tail empty).
              let cpResult3 =
                  CheckpointedReplay.applyFromCheckpoint sink (buildDashboard ()) "eq" 3
                  |> Async.RunSynchronously

              match cpResult3 with
              | Ok tree ->
                  Expect.equal
                      (childIds tree)
                      (childIds midTreeFromGenesis)
                      "Target = checkpoint sequence returns snapshot directly"
              | Error e -> failtestf "Expected Ok at target=3, got %A" e
          }

          test "Checkpoint.create — materialises at sink's current LatestSequence" {
              let sink = InMemorySink.createWithCheckpoints<TestMsg> ()
              let finalTree = populate "create" sink

              let cp = Checkpoint.create sink "create" finalTree |> Async.RunSynchronously

              Expect.equal cp.Sequence 5 "Checkpoint pins at the sink's latest sequence (5)"

              let prevAt5 =
                  match sink.Replay("create", 5, 5) |> Async.RunSynchronously with
                  | r :: _ -> r
                  | [] -> failtest "Expected record at sequence 5"

              Expect.equal cp.PreviousChainHead prevAt5.Hash "PreviousChainHead = latest op's Hash"

              match Checkpoint.verifySnapshotHash cp with
              | Ok() -> ()
              | Error(recomputed, stored) ->
                  failtestf "Snapshot hash mismatch — recomputed=%s, stored=%s" recomputed stored
          }

          test "Snapshot-hash mismatch — verifySnapshotHash detects tampered Snapshot" {
              let sink = InMemorySink.createWithCheckpoints<TestMsg> ()
              let _ = populate "snaptamper" sink

              let tree =
                  match
                      Replay.applyTo (buildDashboard ()) (sink.Replay("snaptamper", 1, 2) |> Async.RunSynchronously)
                  with
                  | Ok t -> t
                  | Error e -> failtestf "Replay failed: %A" e

              let cp = Checkpoint.create sink "snaptamper" tree |> Async.RunSynchronously

              // Hand-build a tampered checkpoint with a wrong snapshot but the
              // original SnapshotHash claim.
              let wrongSnapshot = buildDashboard () // Different from the populated tree

              let tampered: Checkpoint<TestMsg> = { cp with Snapshot = wrongSnapshot }

              match Checkpoint.verifySnapshotHash tampered with
              | Ok() -> failtest "Expected snapshot-hash mismatch"
              | Error(recomputed, stored) ->
                  Expect.notEqual recomputed stored "Recomputed hash differs from stored claim"
          }

          // ─── Phase 412 (A2) — the snapshot hash is bound to its chain position ──

          test "A2 — a valid snapshot + hash does NOT validate at a different chain position" {
              let sink = InMemorySink.createWithCheckpoints<TestMsg> ()
              let _ = populate "a2sub" sink

              let treeAt2 =
                  match Replay.applyTo (buildDashboard ()) (sink.Replay("a2sub", 1, 2) |> Async.RunSynchronously) with
                  | Ok t -> t
                  | Error e -> failtestf "Replay failed: %A" e

              // A genuine checkpoint at sequence 2.
              let cp = Checkpoint.create sink "a2sub" treeAt2 |> Async.RunSynchronously
              Expect.isOk (Checkpoint.verifySnapshotHash cp |> Result.mapError (fun _ -> ())) "genuine cp verifies"

              // Substitution attack: reuse the SAME snapshot + hash but claim a
              // different position (a different head/seq). Pre-412 (hash = sha256
              // tree) this passed — the hash didn't know its position. Now it fails.
              let substituted =
                  { cp with
                      Sequence = 3
                      PreviousChainHead = String.replicate 64 "9" }

              match Checkpoint.verifySnapshotHash substituted with
              | Ok() -> failtest "a snapshot hash must not validate at a substituted (head, seq)"
              | Error _ -> ()
          }

          test "A2 — empty-tail replay anchors PreviousChainHead to the real chain" {
              let sink = InMemorySink.createWithCheckpoints<TestMsg> ()
              let _ = populate "a2anchor" sink

              let treeAt2 =
                  match
                      Replay.applyTo (buildDashboard ()) (sink.Replay("a2anchor", 1, 2) |> Async.RunSynchronously)
                  with
                  | Ok t -> t
                  | Error e -> failtestf "Replay failed: %A" e

              // A checkpoint whose PreviousChainHead is forged (does not match the
              // real chain head at sequence 2), but whose snapshot hash is
              // self-consistent with that forged head. Empty tail (target = 2).
              let forgedHead = String.replicate 64 "e"
              let canonical = CanonicalJson.encodeNode treeAt2

              let forged: Checkpoint<TestMsg> =
                  { StreamId = "a2anchor"
                    Sequence = 2
                    PreviousChainHead = forgedHead
                    SnapshotHash = HashChain.snapshotHash forgedHead 2 canonical
                    Snapshot = treeAt2
                    Timestamp = DateTimeOffset.UtcNow }

              sink.AppendCheckpoint forged |> Async.RunSynchronously

              // Pre-412 the empty-tail path returned the snapshot trusted; now it
              // anchors the head to the real chain and rejects the forged position.
              match
                  CheckpointedReplay.applyFromCheckpoint sink (buildDashboard ()) "a2anchor" 2
                  |> Async.RunSynchronously
              with
              | Error(CheckpointReplayError.BoundaryMismatch _) -> ()
              | other -> failtestf "expected BoundaryMismatch on the forged empty-tail checkpoint, got %A" other
          }

          test "Boundary mismatch — tail with wrong PreviousHash surfaces BoundaryMismatch" {
              let sink = InMemorySink.createWithCheckpoints<TestMsg> ()
              let _ = populate "boundary" sink

              // Checkpoint at sequence 2 BUT with a deliberately-wrong PreviousChainHead.
              let treeAt2 =
                  match
                      Replay.applyTo (buildDashboard ()) (sink.Replay("boundary", 1, 2) |> Async.RunSynchronously)
                  with
                  | Ok t -> t
                  | Error e -> failtestf "Replay failed: %A" e

              let canonical = CanonicalJson.encodeNode treeAt2
              let bogusChainHead = String.replicate 64 "f"
              // A VALID snapshot hash (bound to the bogus head it will carry) so the
              // snapshot-hash check passes and the BOUNDARY check is what fails.
              let snapshotHash = HashChain.snapshotHash bogusChainHead 2 canonical

              let badCp: Checkpoint<TestMsg> =
                  { StreamId = "boundary"
                    Sequence = 2
                    PreviousChainHead = bogusChainHead
                    SnapshotHash = snapshotHash
                    Snapshot = treeAt2
                    Timestamp = DateTimeOffset.UtcNow }

              sink.AppendCheckpoint badCp |> Async.RunSynchronously

              let result =
                  CheckpointedReplay.applyFromCheckpoint sink (buildDashboard ()) "boundary" 5
                  |> Async.RunSynchronously

              match result with
              | Error(CheckpointReplayError.BoundaryMismatch(seqNo, expected, actual)) ->
                  Expect.equal seqNo 2 "Mismatch reported at checkpoint sequence 2"
                  Expect.equal expected bogusChainHead "Expected = tampered PreviousChainHead"
                  Expect.notEqual expected actual "Actual differs from expected"
              | other -> failtestf "Expected BoundaryMismatch, got %A" other
          }

          test "Compaction — retention policy keeps last K checkpoints + truncates older ops" {
              let sink = InMemorySink.createWithCheckpoints<TestMsg> ()
              let _ = populate "compact" sink

              // Materialise three checkpoints at sequences 1, 3, 5 by hand to
              // pin the test against specific op-indices.
              let cpAt (seqNo: int) : Checkpoint<TestMsg> =
                  let tree =
                      match
                          Replay.applyTo
                              (buildDashboard ())
                              (sink.Replay("compact", 1, seqNo) |> Async.RunSynchronously)
                      with
                      | Ok t -> t
                      | Error e -> failtestf "Replay to %d failed: %A" seqNo e

                  let prevHash =
                      match sink.Replay("compact", seqNo, seqNo) |> Async.RunSynchronously with
                      | r :: _ -> r.Hash
                      | [] -> failtestf "No record at sequence %d" seqNo

                  let canonical = CanonicalJson.encodeNode tree
                  let snapshotHash = HashChain.snapshotHash prevHash seqNo canonical

                  { StreamId = "compact"
                    Sequence = seqNo
                    PreviousChainHead = prevHash
                    SnapshotHash = snapshotHash
                    Snapshot = tree
                    Timestamp = DateTimeOffset.UtcNow }

              sink.AppendCheckpoint(cpAt 1) |> Async.RunSynchronously
              sink.AppendCheckpoint(cpAt 3) |> Async.RunSynchronously
              sink.AppendCheckpoint(cpAt 5) |> Async.RunSynchronously

              let policy = CompactionPolicy.keep 2

              let truncated =
                  Compaction.applyPolicy sink "compact" policy |> Async.RunSynchronously

              // Oldest retained checkpoint = sequence 3; ops 1..3 truncated.
              Expect.equal truncated 3 "Three ops truncated (1, 2, 3)"

              let remaining = sink.Replay("compact", 1, 10) |> Async.RunSynchronously
              Expect.equal (remaining |> List.map _.Sequence) [ 4; 5 ] "Sequences 4 + 5 survive"

              let retained = sink.ListCheckpoints "compact" |> Async.RunSynchronously
              Expect.equal (retained |> List.map _.Sequence) [ 3; 5 ] "Last 2 checkpoints retained"

              // Surviving tail must remain chain-verifiable starting from the
              // oldest retained checkpoint's PreviousChainHead.
              let cp3 = retained |> List.head
              let tailFirst = remaining |> List.head
              Expect.equal tailFirst.PreviousHash cp3.PreviousChainHead "Tail links to checkpoint chain head"

              // Replay-from-checkpoint against the truncated stream still works.
              let cpResult =
                  CheckpointedReplay.applyFromCheckpoint sink (buildDashboard ()) "compact" 5
                  |> Async.RunSynchronously

              match cpResult with
              | Ok _ -> ()
              | Error e -> failtestf "Replay-from-checkpoint after compaction failed: %A" e
          }

          test "Retention disabled — KeepCheckpoints ≤ 0 is a no-op" {
              let sink = InMemorySink.createWithCheckpoints<TestMsg> ()
              let _ = populate "noop" sink

              // Add one checkpoint.
              let tree =
                  match Replay.applyTo (buildDashboard ()) (sink.Replay("noop", 1, 3) |> Async.RunSynchronously) with
                  | Ok t -> t
                  | Error e -> failtestf "Replay failed: %A" e

              let _ = Checkpoint.create sink "noop" tree |> Async.RunSynchronously

              let policy = CompactionPolicy.keep 0

              let truncated = Compaction.applyPolicy sink "noop" policy |> Async.RunSynchronously

              Expect.equal truncated 0 "Zero ops truncated when retention disabled"

              let remaining = sink.Replay("noop", 1, 10) |> Async.RunSynchronously
              Expect.equal remaining.Length 5 "All five ops survive"
          }

          test "Compaction — fewer checkpoints than retention threshold is a no-op" {
              let sink = InMemorySink.createWithCheckpoints<TestMsg> ()
              let _ = populate "few" sink

              let tree =
                  match Replay.applyTo (buildDashboard ()) (sink.Replay("few", 1, 4) |> Async.RunSynchronously) with
                  | Ok t -> t
                  | Error e -> failtestf "Replay failed: %A" e

              let _ = Checkpoint.create sink "few" tree |> Async.RunSynchronously

              let policy = CompactionPolicy.keep 5

              let truncated = Compaction.applyPolicy sink "few" policy |> Async.RunSynchronously

              Expect.equal truncated 0 "Zero ops truncated when checkpoints < KeepCheckpoints"
          }

          test "Sqlite — Append + List + Truncate round-trip the new op_checkpoint table" {
              let name = sprintf "fuaran-checkpoint-%s.db" (Guid.NewGuid().ToString("N"))
              let path = Path.Combine(Path.GetTempPath(), name)
              let connStr = sprintf "Data Source=%s" path

              try
                  // Sqlite checkpoint snapshot round-trip needs a node codec.
                  // For this test we don't read the snapshot back (we only verify
                  // schema + truncate + list), so the encodeOnly node codec is
                  // sufficient — ListCheckpoints with the encodeOnly codec would
                  // fail at snapshot decode, so we only test AppendCheckpoint +
                  // TruncateOpsThrough; List/LatestCheckpointAtOrBefore are
                  // exercised against InMemory above.
                  let sink: IOpStreamCheckpointSink<TestMsg> =
                      SqliteSink.createWithCheckpoints connStr testCodec (NodeJsonCodec.encodeOnly ())

                  let op1 = TreeOp.RemoveNode(NodeId "x"): TreeOp<TestMsg>
                  let op2 = TreeOp.RemoveNode(NodeId "y"): TreeOp<TestMsg>
                  let r1 = buildRecord "stream" 1 op1 None (timestamp 100L)
                  let r2 = buildRecord "stream" 2 op2 (Some r1) (timestamp 200L)
                  sink.Append r1 |> Async.RunSynchronously
                  sink.Append r2 |> Async.RunSynchronously

                  // Build a synthetic checkpoint at sequence 2.
                  let cp: Checkpoint<TestMsg> =
                      { StreamId = "stream"
                        Sequence = 2
                        PreviousChainHead = r2.Hash
                        SnapshotHash = String.replicate 64 "a"
                        Snapshot = buildDashboard ()
                        Timestamp = DateTimeOffset.FromUnixTimeSeconds 300L }

                  sink.AppendCheckpoint cp |> Async.RunSynchronously

                  let truncated = sink.TruncateOpsThrough("stream", 1) |> Async.RunSynchronously

                  Expect.equal truncated 1 "One op truncated (sequence 1)"

                  let surviving = sink.Replay("stream", 1, 10) |> Async.RunSynchronously

                  Expect.equal (surviving |> List.map _.Sequence) [ 2 ] "Only sequence 2 survives in the live stream"
              finally
                  try
                      File.Delete path
                  with _ ->
                      ()
          } ]
