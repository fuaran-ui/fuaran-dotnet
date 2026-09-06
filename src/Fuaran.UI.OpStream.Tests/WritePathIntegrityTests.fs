module Fuaran.UI.OpStream.Tests.WritePathIntegrityTests

open System
open System.IO
open Expecto
open Fuaran.UI.Types
open Fuaran.UI.Ops.Types
open Fuaran.UI.OpStream.Abstractions
open Fuaran.UI.OpStream.InMemory
open Fuaran.UI.OpStream.Sqlite
open Fuaran.UI.OpStream.Replay
open Fuaran.UI.Telemetry.Abstractions
open Fuaran.UI.OpStream.Tests.TestSupport

// ============================================================================
//  Phase 1525 — the WRITE path.
//
//  Three properties, and each is written so it can be seen failing against the
//  shape it replaces rather than merely passing against the shape it asserts:
//
//   1. ALLOCATION IS A COMPARE-AND-APPEND. Two writers persisting concurrently
//      to one stream both land, at contiguous sequences. The pre-1525
//      read-then-append form fails this — both computed `LatestSequence + 1`,
//      one won, and the loser's duplicate-sequence refusal was swallowed into a
//      hook whose default was silence.
//
//   2. A BROKEN RECORD IS REFUSED AT THE WRITE. A record whose `Hash` does not
//      recompute, whose `PreviousHash` does not name the head, or whose
//      `Sequence` does not extend the stream is refused by NAME, and nothing is
//      written. Before this it was accepted, and then every subsequent read of
//      the SEGMENT threw — one bad write poisoning a stream permanently, blamed
//      on the record rather than on the writer.
//
//   3. A LOST APPEND IS NEVER REPORTED AS A SUCCESS. The telemetry row is
//      emitted after the append settles and names what the append did.
//
//  Plus the retention horizon (M-C2), actor attribution (M-C4), the atomic
//  batch import (L-A16) and the invocation-index compaction (L-A15).
// ============================================================================

let private removeRight = TreeOp.RemoveNode(NodeId "right"): TreeOp<TestMsg>
let private removeLeft = TreeOp.RemoveNode(NodeId "left"): TreeOp<TestMsg>

let private freshDbPath () : string =
    Path.Combine(Path.GetTempPath(), sprintf "fuaran-write-path-%s.db" (Guid.NewGuid().ToString("N")))

/// A collecting telemetry sink — the rows are the assertion.
type private CollectingTelemetry() =
    let rows = ResizeArray<OpApplyTelemetry>()
    member _.Rows = List.ofSeq rows

    interface IFuaranTelemetrySink with
        member _.RecordOpApply(t: OpApplyTelemetry) = lock rows (fun () -> rows.Add t)
        member _.RecordDeny _ = ()
        member _.RecordRenderFailure _ = ()
        member _.RecordProviderCall _ = ()
        member _.RecordCacheStat _ = ()
        member _.RecordValidateOutcome _ = ()

/// A sink that persists nothing and says so — the "lost append" fixture.
type private RefusingSink<'Msg>(inner: IOpStreamSink<'Msg>) =
    interface IOpStreamSink<'Msg> with
        member _.Append(_record) =
            async { return failwith "RefusingSink: this store is not accepting writes" }

        member _.Replay(streamId, fromSequence, toSequence) =
            inner.Replay(streamId, fromSequence, toSequence)

        member _.LatestSequence streamId = inner.LatestSequence streamId
        member _.Streams() = inner.Streams()

let private refusalMessage (what: string) (act: unit -> unit) : string =
    try
        act ()
        failtestf "GO-RED FAILED: %s admitted a record it should have refused" what
    with :? InvalidOperationException as ex ->
        ex.Message

[<Tests>]
let tests =
    testList
        "Fuaran.UI.OpStream — write-path integrity (Phase 1525)"
        [

          // ── 1. Compare-and-append ─────────────────────────────────────────

          testCase "two concurrent writers on one stream BOTH persist, at contiguous sequences"
          <| fun _ ->
              // The load-bearing test. Against the pre-1525 read-then-append
              // form this fails: both writers read the same `LatestSequence`,
              // both build at the same sequence, and one is refused as a
              // duplicate — silently, because the default sink-error hook was
              // `None` and `None` meant nothing at all.
              let sink = InMemorySink.create<TestMsg> ()
              let streamId = "cas-two-writers"
              let tree = buildDashboard ()

              let ctx =
                  PersistContext.create streamId "tester" |> PersistContext.withSilentSinkErrors

              let write op =
                  async { return! ApplyPersist.applyAndPersistWith sink ctx op tree }

              let results =
                  [ write removeRight; write removeLeft ]
                  |> Async.Parallel
                  |> Async.RunSynchronously

              for r in results do
                  match r with
                  | Ok(_, PersistAttempt.Persisted _) -> ()
                  | Ok(_, PersistAttempt.Failed(_, failure)) ->
                      failtestf "a writer lost its op: %s" (PersistFailure.describe streamId 0 failure)
                  | Error e -> failtestf "the apply itself failed: %A" e

              let records = sink.Replay(streamId, 1, 10) |> Async.RunSynchronously

              Expect.equal records.Length 2 "both writers' records are in the stream"

              Expect.equal
                  (records |> List.map _.Sequence)
                  [ 1; 2 ]
                  "the sequences are contiguous — neither writer overwrote the other"

          testCase "the compare-and-append rebuilds against the head the refusal named"
          <| fun _ ->
              let sink = InMemorySink<TestMsg>() :> IOpStreamCasSink<TestMsg>
              let streamId = "cas-rebuild"

              // Land one record so the head is not genesis.
              let r1 = buildRecord streamId 1 removeRight None (timestamp 100L)

              match sink.AppendIf(r1, HashChain.genesisPreviousHash) |> Async.RunSynchronously with
              | CasAppendOutcome.Appended _ -> ()
              | CasAppendOutcome.StaleHead(e, a) -> failtestf "the genesis append was refused (%s vs %s)" e a

              // A record built against the STALE genesis head is refused, and
              // nothing is written.
              let stale = buildRecord streamId 2 removeLeft None (timestamp 200L)

              match sink.AppendIf(stale, HashChain.genesisPreviousHash) |> Async.RunSynchronously with
              | CasAppendOutcome.StaleHead(expected, actual) ->
                  Expect.equal expected HashChain.genesisPreviousHash "the refusal echoes what was expected"
                  Expect.equal actual r1.Hash "the refusal NAMES the head the store actually holds"
              | CasAppendOutcome.Appended _ -> failtest "GO-RED FAILED: a stale-head append was accepted"

              Expect.equal
                  ((sink :> IOpStreamSink<TestMsg>).LatestSequence streamId
                   |> Async.RunSynchronously)
                  1
                  "the refused append persisted nothing"

          // ── 2. Admission ──────────────────────────────────────────────────

          testCase "InMemorySink refuses a record whose Hash does not recompute, and names the check"
          <| fun _ ->
              let sink = InMemorySink.create<TestMsg> ()
              let r1 = buildRecord "adm-hash" 1 removeRight None (timestamp 100L)

              let broken =
                  { r1 with
                      Timestamp = r1.Timestamp.AddSeconds 1.0 }

              let message =
                  refusalMessage "InMemorySink.Append" (fun () -> sink.Append broken |> Async.RunSynchronously)

              Expect.stringContains message "adm-hash" "names the stream"
              Expect.stringContains message "Hash does not recompute" "names the failed check"
              Expect.stringContains message "Nothing was written" "says the stream is untouched"

              Expect.equal
                  (sink.LatestSequence "adm-hash" |> Async.RunSynchronously)
                  0
                  "and the stream really is untouched"

          testCase "InMemorySink refuses a record whose PreviousHash does not name the head"
          <| fun _ ->
              let sink = InMemorySink.create<TestMsg> ()
              let r1 = buildRecord "adm-prev" 1 removeRight None (timestamp 100L)
              sink.Append r1 |> Async.RunSynchronously

              // Chained to genesis rather than to r1 — the shape a second writer
              // that never read the head produces.
              let misChained = buildRecord "adm-prev" 2 removeLeft None (timestamp 200L)

              let message =
                  refusalMessage "InMemorySink.Append" (fun () -> sink.Append misChained |> Async.RunSynchronously)

              Expect.stringContains message "PreviousHash does not name the stream's head" "names the failed check"

              Expect.equal
                  (sink.LatestSequence "adm-prev" |> Async.RunSynchronously)
                  1
                  "the refused append persisted nothing"

              // And the stream is still readable — the property the pre-1525
              // accept-then-refuse-on-read behaviour destroyed.
              Expect.equal
                  (sink.Replay("adm-prev", 1, 10) |> Async.RunSynchronously |> List.length)
                  1
                  "the stream survives the refused append"

          testCase "SqliteSink refuses a broken record at the write, and the store survives it"
          <| fun _ ->
              let path = freshDbPath ()

              try
                  let sink: IOpStreamSink<TestMsg> =
                      SqliteSink.create (sprintf "Data Source=%s" path) testCodec

                  let r1 = buildRecord "sql-adm" 1 removeRight None (timestamp 100L)
                  sink.Append r1 |> Async.RunSynchronously

                  let misChained = buildRecord "sql-adm" 2 removeLeft None (timestamp 200L)

                  let message =
                      refusalMessage "SqliteSink.Append" (fun () -> sink.Append misChained |> Async.RunSynchronously)

                  Expect.stringContains message "SqliteSink" "names the sink"
                  Expect.stringContains message "sql-adm" "names the stream"

                  Expect.equal
                      (sink.Replay("sql-adm", 1, 10) |> Async.RunSynchronously |> List.length)
                      1
                      "the durable store survives the refused append"
              finally
                  // WAL leaves the pooled connection holding the file; the repo's
                  // other Sqlite tests clear the pool for exactly this reason.
                  Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools()

                  if File.Exists path then
                      File.Delete path

          testCase "WriteAdmission.Off is real, and has to be named"
          <| fun _ ->
              // The escape hatch, asserted rather than described — so anyone who
              // changes the default has to come here and change a test that says
              // what the default is.
              let sink =
                  InMemorySink.createWithModes<TestMsg> LoadVerification.Off WriteAdmission.Off

              let r1 = buildRecord "adm-off" 1 removeRight None (timestamp 100L)

              let broken =
                  { r1 with
                      Timestamp = r1.Timestamp.AddSeconds 1.0 }

              sink.Append broken |> Async.RunSynchronously

              Expect.equal (sink.LatestSequence "adm-off" |> Async.RunSynchronously) 1 "Off admits what Full refuses"

          // ── 3. A lost append is never a success ───────────────────────────

          testCase "a lost append emits PersistLost, never Applied, and the row names the reason"
          <| fun _ ->
              let inner = InMemorySink.create<TestMsg> ()
              let sink = RefusingSink<TestMsg>(inner) :> IOpStreamSink<TestMsg>
              let telemetry = CollectingTelemetry()

              let ctx =
                  PersistContext.create "lost" "tester" |> PersistContext.withSilentSinkErrors

              let result =
                  ApplyPersist.applyWithSinks sink telemetry ctx removeRight (buildDashboard ())
                  |> Async.RunSynchronously

              Expect.isOk result "the apply itself succeeded, and that is what is returned"

              match telemetry.Rows with
              | [ row ] ->
                  match row.Outcome with
                  | OpOutcome.PersistLost reason ->
                      Expect.stringContains reason "NOT persisted" "the row says the op is not durable"
                  | OpOutcome.Applied ->
                      failtest "GO-RED FAILED: the telemetry row claimed Applied for an op the sink lost"
                  | other -> failtestf "expected PersistLost, got %A" other
              | rows -> failtestf "expected exactly one telemetry row, got %d" rows.Length

          testCase "a lost append reaches the sink-error hook, and silence has to be asked for by name"
          <| fun _ ->
              let inner = InMemorySink.create<TestMsg> ()
              let sink = RefusingSink<TestMsg>(inner) :> IOpStreamSink<TestMsg>
              let seen = ResizeArray<exn>()

              let ctx =
                  PersistContext.create "hooked" "tester"
                  |> PersistContext.withSinkErrorHook seen.Add

              ApplyPersist.applyAndPersist sink ctx removeRight (buildDashboard ())
              |> Async.RunSynchronously
              |> ignore

              Expect.equal seen.Count 1 "the hook was told about the loss"

              match seen[0] with
              | PersistFailedException(streamId, _, PersistFailure.SinkRefused _) ->
                  Expect.equal streamId "hooked" "the typed failure names the stream"
              | other -> failtestf "expected a typed PersistFailedException, got %A" other

          // ── M-C4. Actor attribution ───────────────────────────────────────

          testCase "the context's actor reaches the record, and UserId is lifted only when it is absent"
          <| fun _ ->
              let sink = InMemorySink.create<TestMsg> ()

              let agentCtx =
                  PersistContext.create "actor-agent" "tester"
                  |> PersistContext.withActor (Actor.Agent("test-model", "v1", "planner"))
                  |> PersistContext.withPromptId "prompt-7"

              ApplyPersist.applyAndPersist sink agentCtx removeRight (buildDashboard ())
              |> Async.RunSynchronously
              |> ignore

              match sink.Replay("actor-agent", 1, 1) |> Async.RunSynchronously with
              | [ r ] ->
                  Expect.equal
                      r.Actor
                      (Actor.Agent("test-model", "v1", "planner"))
                      "the host's actor was recorded, not a derived Human"

                  Expect.equal r.PromptId (Some "prompt-7") "the prompt id reached the record"
              | other -> failtestf "expected one record, got %d" other.Length

              // Absent: the legacy lift, byte-identical to the pre-1525 record.
              let plainCtx = PersistContext.create "actor-plain" "tester"

              ApplyPersist.applyAndPersist sink plainCtx removeLeft (buildDashboard ())
              |> Async.RunSynchronously
              |> ignore

              match sink.Replay("actor-plain", 1, 1) |> Async.RunSynchronously with
              | [ r ] -> Expect.equal r.Actor (Actor.ofLegacyString "tester") "UserId is lifted when no actor is named"
              | other -> failtestf "expected one record, got %d" other.Length

          // ── M-C2. The retention horizon ───────────────────────────────────

          testCase "replay below the retention horizon REFUSES rather than returning the initial tree"
          <| fun _ ->
              // The defect, exactly: compact a stream, then ask for the state at
              // a sequence whose ops are gone. The pre-1525 path folded the
              // surviving ops over `initialTree` and returned `Ok` — the empty
              // document presented as the state at op 2, indistinguishable from
              // a document that was genuinely empty then.
              let sink = InMemorySink.createWithCheckpoints<TestMsg> ()
              let streamId = "horizon"
              let tree = buildDashboard ()

              let ctx =
                  PersistContext.create streamId "tester" |> PersistContext.withSilentSinkErrors

              let baseSink = sink :> IOpStreamSink<TestMsg>

              let after1 =
                  ApplyPersist.applyAndPersist baseSink ctx removeRight tree
                  |> Async.RunSynchronously
                  |> Result.defaultWith (fun e -> failtestf "apply 1 failed: %A" e)

              let after2 =
                  ApplyPersist.applyAndPersist baseSink ctx removeLeft after1
                  |> Async.RunSynchronously
                  |> Result.defaultWith (fun e -> failtestf "apply 2 failed: %A" e)

              // Checkpoint at 2, then truncate the ops it covers.
              Checkpoint.create sink streamId after2 |> Async.RunSynchronously |> ignore

              sink.TruncateOpsThrough(streamId, 2) |> Async.RunSynchronously |> ignore
              sink.TruncateCheckpointsBefore(streamId, 2) |> Async.RunSynchronously |> ignore

              // Asking for the state at op 1 is now unanswerable: op 1 is gone
              // and the only checkpoint sits at 2.
              match
                  CheckpointedReplay.applyFromCheckpoint sink tree streamId 1
                  |> Async.RunSynchronously
              with
              | Error(CheckpointReplayError.BelowRetentionHorizon(target, earliest)) ->
                  Expect.equal target 1 "the refusal names the target that could not be reached"
                  Expect.isNone earliest "and reports that nothing at or below the target survives"
              | Ok _ -> failtest "GO-RED FAILED: a tree was returned as the state at a sequence whose ops are gone"
              | other -> failtestf "expected BelowRetentionHorizon, got %A" other

          testCase "a stream that simply has no records is still legitimately the initial tree"
          <| fun _ ->
              // The discrimination that makes the horizon check honest: "no ops
              // below the target" is only an error when ops were REMOVED.
              let sink = InMemorySink.createWithCheckpoints<TestMsg> ()
              let tree = buildDashboard ()

              match
                  CheckpointedReplay.applyFromCheckpoint sink tree "empty-stream" 5
                  |> Async.RunSynchronously
              with
              | Ok t -> Expect.equal t.Id tree.Id "an empty stream replays to the initial tree, as it always did"
              | Error e -> failtestf "an empty stream was refused: %A" e

          // ── L-A15. The invocation index compacts with the records ─────────

          testCase "a truncated record's invocation key stops answering as a duplicate"
          <| fun _ ->
              // Left behind, the key answers a later append with a receipt naming
              // a sequence the stream no longer holds — the caller is told its op
              // is durable, and `Replay` at that address returns nothing.
              let sink = InMemorySink<TestMsg>()
              let keyed = sink :> IOpStreamKeyedSink<TestMsg>
              let baseSink = sink :> IOpStreamSink<TestMsg>
              let checkpoints = sink :> IOpStreamCheckpointSink<TestMsg>
              let streamId = "key-compaction"

              let r1 = buildRecord streamId 1 removeRight None (timestamp 100L)

              match keyed.AppendKeyed(r1, "invocation-A") |> Async.RunSynchronously with
              | KeyedAppendOutcome.Appended _ -> ()
              | KeyedAppendOutcome.Duplicate _ -> failtest "the first append under a fresh key is not a duplicate"

              checkpoints.TruncateOpsThrough(streamId, 1) |> Async.RunSynchronously |> ignore

              Expect.equal (baseSink.LatestSequence streamId |> Async.RunSynchronously) 0 "the record is gone"

              // The same key must now be writable again, not answered from a
              // receipt naming a record that no longer exists.
              let fresh = buildRecord streamId 1 removeLeft None (timestamp 200L)

              match keyed.AppendKeyed(fresh, "invocation-A") |> Async.RunSynchronously with
              | KeyedAppendOutcome.Appended receipt ->
                  Expect.equal receipt.Sequence 1 "the key was free, and the record landed"
              | KeyedAppendOutcome.Duplicate receipt ->
                  failtestf
                      "GO-RED FAILED: a truncated record's key still answered as a duplicate at sequence %d"
                      receipt.Sequence

          // ── L-A16 / M-C2. The atomic batch and the atomic compaction ──────

          testCase "AppendAll is all-or-nothing — a refused record leaves the stream untouched"
          <| fun _ ->
              let sink = InMemorySink<TestMsg>()
              let batch = sink :> IOpStreamBatchSink<TestMsg>
              let baseSink = sink :> IOpStreamSink<TestMsg>
              let streamId = "batch-atomic"

              let r1 = buildRecord streamId 1 removeRight None (timestamp 100L)
              let r2 = buildRecord streamId 2 removeLeft (Some r1) (timestamp 200L)
              // Chained to genesis instead of to r2 — the third record is broken.
              let r3 = buildRecord streamId 3 removeRight None (timestamp 300L)

              Expect.throws
                  (fun () -> batch.AppendAll [ r1; r2; r3 ] |> Async.RunSynchronously)
                  "the batch is refused when any record in it is"

              Expect.equal
                  (baseSink.LatestSequence streamId |> Async.RunSynchronously)
                  0
                  "and NOTHING from the batch is in the stream — not even the two good records"

              // The good prefix on its own still lands, so the refusal is about
              // the broken record and not about batching.
              batch.AppendAll [ r1; r2 ] |> Async.RunSynchronously

              Expect.equal
                  (baseSink.LatestSequence streamId |> Async.RunSynchronously)
                  2
                  "a well-formed batch lands whole"

          testCase "Compact removes the ops and the checkpoints that justified removing them, in one step"
          <| fun _ ->
              let sink = InMemorySink<TestMsg>()
              let compact = sink :> IOpStreamCompactSink<TestMsg>
              let checkpoints = sink :> IOpStreamCheckpointSink<TestMsg>
              let baseSink = sink :> IOpStreamSink<TestMsg>
              let streamId = "compact-atomic"

              let r1 = buildRecord streamId 1 removeRight None (timestamp 100L)
              let r2 = buildRecord streamId 2 removeLeft (Some r1) (timestamp 200L)
              baseSink.Append r1 |> Async.RunSynchronously
              baseSink.Append r2 |> Async.RunSynchronously

              let cp0: Checkpoint<TestMsg> =
                  { StreamId = streamId
                    Sequence = 1
                    PreviousChainHead = r1.Hash
                    SnapshotHash = "unused-here"
                    Snapshot = buildDashboard ()
                    Timestamp = timestamp 150L }

              checkpoints.AppendCheckpoint cp0 |> Async.RunSynchronously

              let ops, cps = compact.Compact(streamId, 2) |> Async.RunSynchronously

              Expect.equal ops 2 "both ops were removed"
              Expect.equal cps 1 "and the checkpoint below the horizon with them"

              Expect.equal
                  (checkpoints.ListCheckpoints streamId |> Async.RunSynchronously |> List.length)
                  0
                  "no checkpoint is left claiming to cover records that are gone" ]
