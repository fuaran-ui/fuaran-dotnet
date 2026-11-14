module Fuaran.UI.OpStream.Dag.Tests.WriteIntegrityTests

open System
open System.IO
open Expecto
open Microsoft.Data.Sqlite
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Ops
open Fuaran.UI.Ops.Types
open Fuaran.UI.OpStream.Abstractions
open Fuaran.UI.OpStream.Dag.Abstractions
open Fuaran.UI.OpStream.Dag.InMemory
open Fuaran.UI.OpStream.Dag.Sqlite
open Fuaran.UI.OpStream.Dag.Merge
open Fuaran.UI.OpStream.TestSupport

// ============================================================================
//  Phase 1525 — VERIFY ON WRITE, on both DAG sinks.
//
//  Three defects, one suite. Each test states what the pre-1525 sink did with
//  its input, because in every case the answer was "accepted it, quietly":
//
//   1. **A collision was mistaken for a duplicate.** `Add` compared the parents
//      and the merge outcome hash and nothing else, so a record with the SAME hash and a
//      DIFFERENT `Op` matched, was classified as an idempotent re-append, and was
//      dropped. Two records claiming one address is the definition of a content-
//      addressing violation; dropping the second is the one response that leaves
//      no evidence it ever arrived.
//
//   2. **A dangling parent was admitted.** A record naming a parent no stream
//      held went in, and the edge stayed broken until some later `Records` read
//      — or a replay — tripped over it, long after the write that made it.
//
//   3. **A tombstone destroyed idempotence.** Retention pruned part of the
//      address-bearing state with the payload, so an identical re-add of a swept
//      record no longer matched what the store held. Since Phase 2645 a pruned
//      node keeps its address in the sink's skeleton, and a re-add is a no-op.
//
//  Both sinks are tested for each, because "the in-memory one behaves like the
//  durable one" is the property every other test in this suite quietly assumes.
// ============================================================================

let private brand =
    TreeOp.UpdateStyle(
        leftChildId,
        { Defaults.style with
            Tone = ToneVariant.Brand }
    )

let private success =
    TreeOp.UpdateStyle(
        rightChildId,
        { Defaults.style with
            Tone = ToneVariant.Success }
    )

let private removeRight = TreeOp.RemoveNode rightChildId

/// A style edit to the LEFT pane — the pane `removeRight` does NOT touch.
///
/// Phase 1526: `success` retones the RIGHT pane, so pairing it with
/// `removeRight` is one side editing a node the other side deletes. That pair
/// used to auto-merge, silently discarding the edit, and it is now the
/// `DeleteModify` refusal Phase 179 declared and nothing raised. The merge
/// fixtures below need SOME auto-merging branch pair and are about checkpoints,
/// attribution and pruning rather than about conflict classes, so they take a
/// genuinely disjoint pair instead of one that only merged because a conflict
/// was being dropped.
let private criticalLeft =
    TreeOp.UpdateStyle(
        leftChildId,
        { Defaults.style with
            Tone = ToneVariant.Critical }
    )

let private connectionString (path: string) = sprintf "Data Source=%s" path

/// Run `write`, requiring it to refuse; returns the refusal message.
let private refusal (what: string) (write: unit -> unit) : string =
    try
        write ()
        failtestf "GO-RED FAILED: %s accepted the record instead of refusing it" what
    with :? InvalidOperationException as ex ->
        ex.Message

/// A record that CLAIMS `original`'s content address while carrying a different
/// op — the forged/corrupt shape a collision actually takes. (A genuine SHA-256
/// collision is not something a test can produce, and is not what this check is
/// defending against: it defends against a record whose stored address is not the
/// address of its content.)
/// The canonical structural op decoder — the one the DAG wire form's nested `op`
/// is written with (`CanonicalJson.encodeOp`), read at the erased `'Msg` the
/// decoder produces. (The SQLite legs use `TestSupport.testCodec`, the same
/// reference pair retyped onto `TestMsg`.)
let private canonicalDecodeOp (s: string) : Result<TreeOp<obj>, string> =
    JsonDecode.decodeOp s |> Result.mapError (sprintf "%A")

let private sameAddressDifferentOp (original: DagOpRecord<TestMsg>) (otherOp: TreeOp<TestMsg>) : DagOpRecord<TestMsg> =
    { original with
        Node =
            { original.Node with
                Op = { original.Entry with Op = otherOp } } }

/// A wire record (the canonical op decoder's erased `'Msg`) with an arbitrary,
/// non-recomputing address — what the envelope decode tests round-trip.
let private wireRecord (envelope: OpResultEnvelope) : DagOpRecord<obj> =
    { StreamId = "s"
      Node =
        { Id = "h1"
          Parents = []
          Actor = Actor.Human "tester"
          Op =
            { Op = TreeOp.RemoveNode(NodeId "right")
              PromptId = None
              Timestamp = timestamp 1L
              ResultEnvelope = envelope } } }

[<Tests>]
let tests =
    testList
        "Dag — verify on write (Phase 1525)"
        [

          // ── 1. Parent presence ───────────────────────────────────────────

          test "InMemoryDagSink REFUSES a record naming a parent the store does not hold" {
              let sink = InMemoryDagSink.create<TestMsg> ()
              let absentParent = String.replicate 64 "c"

              let orphan =
                  DagOpRecord.create
                      "s"
                      [ absentParent ]
                      removeRight
                      None
                      (Actor.Human "tester")
                      (timestamp 1L)
                      OpResultEnvelope.Success

              let message = refusal "InMemoryDagSink.Add" (fun () -> add sink orphan)

              Expect.stringContains message "parent-presence check failed" "names the check that failed"
              Expect.stringContains message absentParent "names WHICH parent is missing"
              Expect.stringContains message orphan.Hash "names the record"
              Expect.stringContains message "'s'" "names the stream"

              // The refusal left nothing behind.
              Expect.isEmpty (sink.Records "s" |> Async.RunSynchronously) "the refused record was not stored"
          }

          test "SqliteDagSink REFUSES a record naming a parent the store does not hold" {
              use db = TempDb.create "dag-write"
              let path = db.Path

              let sink = SqliteDagSink.create<TestMsg> (connectionString path) testCodec

              let absentParent = String.replicate 64 "d"

              let orphan =
                  DagOpRecord.create
                      "s"
                      [ absentParent ]
                      removeRight
                      None
                      (Actor.Human "tester")
                      (timestamp 1L)
                      OpResultEnvelope.Success

              let message = refusal "SqliteDagSink.Add" (fun () -> add sink orphan)

              Expect.stringContains message "parent-presence check failed" "names the check that failed"
              Expect.stringContains message absentParent "names WHICH parent is missing"
              Expect.stringContains message orphan.Hash "names the record"
              Expect.stringContains message "'s'" "names the stream"

              Expect.isEmpty
                  (sink.Records "s" |> Async.RunSynchronously)
                  "the refused record was not stored — the IMMEDIATE transaction rolled back"
          }

          test "a CROSS-STREAM parent still satisfies the write-path check (the guest-fork shape)" {
              // The write check resolves store-wide for the same reason the read
              // check does: a guest branch's genesis legitimately hangs off a
              // record in the HOST stream. A stream-scoped check would refuse
              // every healthy fork, which is a worse defect than the one it fixes.
              let sink = InMemoryDagSink.create<TestMsg> ()
              let hostRecord = stepRecord "host" None brand 1L
              add sink hostRecord

              let guestGenesis =
                  DagOpRecord.create
                      "guest-g1"
                      [ hostRecord.Hash ]
                      removeRight
                      None
                      (Actor.Human "tester")
                      (timestamp 2L)
                      OpResultEnvelope.Success

              add sink guestGenesis

              Expect.equal
                  (sink.Records "guest-g1" |> Async.RunSynchronously |> List.length)
                  1
                  "the guest branch is admitted"
          }

          // ── 2. Collision vs duplicate ────────────────────────────────────

          test "InMemoryDagSink REFUSES a same-address record carrying a different op" {
              let sink = InMemoryDagSink.create<TestMsg> ()
              let original = stepRecord "s" None brand 1L
              add sink original

              // Identical parents — everything the pre-1525 check looked at
              // agrees. Only the OP differs, which is precisely what it could not see.
              let impostor = sameAddressDifferentOp original removeRight
              Expect.equal impostor.Hash original.Hash "the impostor claims the same address"
              Expect.equal impostor.Parents original.Parents "…with identical parents"

              let message = refusal "InMemoryDagSink.Add" (fun () -> add sink impostor)

              Expect.stringContains message "content-address check failed" "names the check that failed"
              Expect.stringContains message original.Hash "names the contested address"
              Expect.stringContains message "'s'" "names the stream"

              // The store still holds the ORIGINAL, unaltered — a refused write
              // must not half-land.
              match sink.TryGet("s", original.Hash) |> Async.RunSynchronously with
              | Some stored ->
                  Expect.equal
                      (CanonicalJson.encodeOp stored.Op)
                      (CanonicalJson.encodeOp original.Op)
                      "the stored record is untouched"
              | None -> failtest "the original record disappeared"
          }

          test "SqliteDagSink REFUSES a same-address record carrying a different op" {
              use db = TempDb.create "dag-write"
              let path = db.Path

              let sink = SqliteDagSink.create<TestMsg> (connectionString path) testCodec

              let original = stepRecord "s" None brand 1L
              add sink original

              let impostor = sameAddressDifferentOp original removeRight
              Expect.equal impostor.Hash original.Hash "the impostor claims the same address"

              let message = refusal "SqliteDagSink.Add" (fun () -> add sink impostor)

              Expect.stringContains message "content-address check failed" "names the check that failed"
              Expect.stringContains message original.Hash "names the contested address"

              // Read back under `LoadVerification.Off`: the load's verification
              // (D32) would refuse the forged record for an unrelated reason,
              // and this assertion is about the WRITE not having landed.
              let unchecked' =
                  SqliteDagSink.createWith<TestMsg> LoadVerification.Off (connectionString path) testCodec

              match unchecked'.TryGet("s", original.Hash) |> Async.RunSynchronously with
              | Some stored ->
                  Expect.equal
                      (CanonicalJson.encodeOp stored.Op)
                      (CanonicalJson.encodeOp original.Op)
                      "the stored row is untouched"
              | None -> failtest "the original row disappeared"
          }

          test "an identical re-add is still the idempotent no-op it always was (both sinks)" {
              // The collision refusal must not have been bought by refusing
              // legitimate re-appends — content addressing makes those a no-op and
              // callers rely on it.
              let mem = InMemoryDagSink.create<TestMsg> ()
              let g = stepRecord "s" None brand 1L
              add mem g
              add mem g
              add mem g
              Expect.equal (mem.Records "s" |> Async.RunSynchronously |> List.length) 1 "in-memory: one record"

              use db = TempDb.create "dag-write"
              let path = db.Path

              let sql = SqliteDagSink.create<TestMsg> (connectionString path) testCodec
              add sql g
              add sql g
              add sql g
              Expect.equal (sql.Records "s" |> Async.RunSynchronously |> List.length) 1 "sqlite: one row"
          }

          // ── 3. Idempotence across a retention sweep ──────────────────────

          test "InMemoryDagSink: an identical re-add AFTER pruning is still a duplicate" {
              let sink = InMemoryDagSink.create<TestMsg> ()
              let g = stepRecord "s" None brand 1L
              let a = stepRecord "s" (Some g) success 2L
              add sink g
              add sink a

              Expect.isTrue (sink.Tombstone("s", a.Hash) |> Async.RunSynchronously) "a is pruned"

              // The re-add must neither throw (a false collision) nor resurrect the
              // pruned payload: the address is held, in the skeleton.
              add sink a

              Expect.equal
                  (sink.Records "s" |> Async.RunSynchronously |> List.map _.Hash)
                  [ g.Hash ]
                  "the re-add did not resurrect the pruned payload"

              Expect.equal
                  (sink.Skeleton "s" |> Async.RunSynchronously |> List.map _.Hash)
                  [ a.Hash ]
                  "a is still exactly one skeleton node"
          }

          test "SqliteDagSink: an identical re-add AFTER pruning is still a duplicate" {
              use db = TempDb.create "dag-write"
              let path = db.Path

              let sink = SqliteDagSink.create<TestMsg> (connectionString path) testCodec

              let g = stepRecord "s" None brand 1L
              let a = stepRecord "s" (Some g) success 2L
              add sink g
              add sink a

              Expect.isTrue (sink.Tombstone("s", a.Hash) |> Async.RunSynchronously) "a is pruned"

              add sink a

              Expect.equal
                  (sink.Records "s" |> Async.RunSynchronously |> List.map _.Hash)
                  [ g.Hash ]
                  "the re-add did not resurrect the pruned payload"

              Expect.equal
                  (sink.Skeleton "s" |> Async.RunSynchronously |> List.map _.Hash)
                  [ a.Hash ]
                  "a is still exactly one skeleton row"
          }

          test "a pruned MERGE node's re-add resolves too" {
              // A merge node is an ordinary node (Phase 2645): pruned, its address and
              // both parent links stay in the skeleton, so its re-add is the duplicate it is.
              let sink = InMemoryDagSink.create<TestMsg> ()
              let initial = buildDashboard ()
              let a = stepRecord "s" None brand 1L
              add sink a
              let branchA = stepRecord "s" (Some a) criticalLeft 2L
              let branchB = stepRecord "s" (Some a) removeRight 3L
              add sink branchA
              add sink branchB

              let mergeRecord =
                  match
                      DagMerge.merge sink "s" initial branchA.Hash branchB.Hash (timestamp 9L)
                      |> Async.RunSynchronously
                  with
                  | MergeResult.Merged(record, _) -> record
                  | other -> failtestf "expected Merged, got %A" other

              add sink mergeRecord

              Expect.isTrue (sink.Tombstone("s", mergeRecord.Hash) |> Async.RunSynchronously) "the merge node is pruned"

              Expect.equal
                  (sink.Skeleton "s"
                   |> Async.RunSynchronously
                   |> List.map (fun n -> n.Hash, n.Parents))
                  [ mergeRecord.Hash, mergeRecord.Parents ]
                  "the merge node's address and both parent links survived the sweep"

              add sink mergeRecord

              Expect.equal
                  (sink.Records "s" |> Async.RunSynchronously |> List.length)
                  3
                  "a + branchA + branchB — the re-add neither resurrected the payload nor copied it"
          }

          // ── 4. The transaction MODE (deferred vs immediate) ──────────────

          test "SqliteDagSink.Add's compare-and-append runs in an IMMEDIATE transaction" {
              // GO-RED PROBE for the transaction mode. Revert `Add`'s
              // `conn.BeginTransaction(false)` (IMMEDIATE) to
              // `conn.BeginTransaction(true)` (DEFERRED) and this test fails.
              //
              // How it discriminates. `Add` is a read-then-write: it checks parent
              // presence, then inserts. Under IMMEDIATE the write lock is taken at
              // BEGIN, so the check reads a state nobody can be midway through
              // changing. Under DEFERRED no lock is taken until the INSERT, so the
              // check reads a stale snapshot and the record it is about to write
              // is justified by a state that no longer holds.
              //
              // The fixture makes that concrete rather than statistical: an
              // external connection holds the write lock while inserting `a`, and
              // commits only AFTER the sink has been asked to add `b` — whose
              // parent is `a`.
              //
              //   IMMEDIATE: `Add(b)` parks at BEGIN under `busy_timeout` until the
              //              external commit lands, then sees `a` and admits `b`.
              //   DEFERRED:  `Add(b)` begins at once, its parent check reads the
              //              pre-commit snapshot, does not see `a`, and refuses.
              //
              // So the deferred shape fails LOUDLY here, with the parent-presence
              // refusal naming a parent that was in fact committed before its
              // write would have run — which is exactly the class of wrongness a
              // deferred read-then-write produces.
              use db = TempDb.create "dag-write"
              let path = db.Path

              let sink = SqliteDagSink.create<TestMsg> (connectionString path) testCodec

              let g = stepRecord "s" None brand 1L
              let a = stepRecord "s" (Some g) success 2L
              let b = stepRecord "s" (Some a) removeRight 3L
              add sink g

              use ext = new SqliteConnection(connectionString path)
              ext.Open()
              // IMMEDIATE: take the write lock now and hold it.
              let extTx = ext.BeginTransaction(false)

              use insert = ext.CreateCommand()

              insert.CommandText <-
                  """INSERT INTO dag_node
    (stream_id, hash, parents_json, op_json, prompt_id, actor_json, timestamp, result_envelope_json)
VALUES
    (@s, @h, @parents, @op, NULL, @user, @ts, @env);"""

              insert.Parameters.AddWithValue("@s", a.StreamId) |> ignore
              insert.Parameters.AddWithValue("@h", a.Hash) |> ignore
              insert.Parameters.AddWithValue("@parents", sprintf "[\"%s\"]" g.Hash) |> ignore
              insert.Parameters.AddWithValue("@op", testCodec.EncodeOp a.Op) |> ignore
              insert.Parameters.AddWithValue("@user", Actor.encode a.Actor) |> ignore

              insert.Parameters.AddWithValue("@ts", a.Timestamp.ToUnixTimeSeconds()) |> ignore

              insert.Parameters.AddWithValue("@env", "{\"$type\":\"Success\"}") |> ignore

              Expect.equal (insert.ExecuteNonQuery()) 1 "the external writer inserted `a` (uncommitted)"

              // Ask the sink to add `b` while the lock is still held.
              let adding = sink.Add b |> Async.StartAsTask

              // Long enough for the DEFERRED shape to have begun, read and
              // refused; the IMMEDIATE shape is still parked at BEGIN.
              System.Threading.Thread.Sleep 750
              extTx.Commit()

              if not (adding.Wait(TimeSpan.FromSeconds 30.0)) then
                  failtest "Add did not complete within 30s of the external commit"

              // `.Result` rethrows the deferred shape's parent-presence
              // refusal, which is the go-red.
              adding.GetAwaiter().GetResult()

              let unchecked' =
                  SqliteDagSink.createWith<TestMsg> LoadVerification.Off (connectionString path) testCodec

              Expect.isSome
                  (unchecked'.TryGet("s", b.Hash) |> Async.RunSynchronously)
                  "`b` landed: its parent check saw the committed `a`"
          }

          // ── 5. The op-result envelope decode (typed, not substring) ──────

          test "DagWire decodes a FAILURE whose message mentions the word Success" {
              // The decoder used to decide the case by testing whether the raw
              // envelope text CONTAINED `"Success"`, so this record — a genuine
              // failure quoting the word — decoded as a success. Nothing
              // downstream could tell the difference, which is what made it worth
              // fixing rather than merely tidying.
              let poisoned =
                  OpResultEnvelope.Failure("APPLY_FAILED", "the \"Success\" branch was not taken")

              let record = wireRecord poisoned

              match DagWire.decodeRecord canonicalDecodeOp (DagWire.encodeRecord CanonicalJson.encodeOp record) with
              | Ok decoded -> Expect.equal decoded.ResultEnvelope poisoned "the failure round-trips as a FAILURE"
              | Error e -> failtestf "decode failed: %s" e
          }

          test "DagWire REFUSES an envelope whose $type it does not recognise" {
              let record = wireRecord OpResultEnvelope.Success

              let encoded =
                  (DagWire.encodeRecord CanonicalJson.encodeOp record)
                      .Replace("{\"$type\":\"Success\"}", "{\"$type\":\"Sideways\"}")

              match DagWire.decodeRecord canonicalDecodeOp encoded with
              | Ok r -> failtestf "expected a refusal, decoded %A" r.ResultEnvelope
              | Error e -> Expect.stringContains e "unrecognised $type" "names what it could not read"
          }

          // ── 6. Merge-node attribution ────────────────────────────────────

          test "a merge node carries the CALLER's actor and prompt id" {
              let sink = InMemoryDagSink.create<TestMsg> ()
              let initial = buildDashboard ()
              let a = stepRecord "s" None brand 1L
              add sink a
              let branchA = stepRecord "s" (Some a) criticalLeft 2L
              let branchB = stepRecord "s" (Some a) removeRight 3L
              add sink branchA
              add sink branchB

              let attribution =
                  { Actor = Actor.Agent("test-model", "v1", "session-42")
                    Prompt = Some "prompt-77"
                    ResultEnvelope = OpResultEnvelope.Success }

              let context =
                  { MergeContext.defaults<TestMsg> with
                      Attribution = attribution }

              let outcome =
                  DagMerge.mergeWith context sink "s" initial branchA.Hash branchB.Hash (timestamp 9L)
                  |> Async.RunSynchronously

              match outcome.Result with
              | MergeResult.Merged(record, _) ->
                  Expect.equal record.Actor attribution.Actor "the merge node is authored by the caller's actor"
                  Expect.equal record.PromptId (Some "prompt-77") "…and carries the caller's prompt id"

                  // The attribution is INSIDE the content address (Phase 1144), so
                  // an attributed merge is a different node from an anonymous one
                  // — which is the whole point of threading it rather than
                  // recording it beside the record.
                  let anonymous =
                      match
                          DagMerge.merge sink "s" initial branchA.Hash branchB.Hash (timestamp 9L)
                          |> Async.RunSynchronously
                      with
                      | MergeResult.Merged(r, _) -> r
                      | other -> failtestf "expected Merged, got %A" other

                  Expect.notEqual record.Hash anonymous.Hash "attribution changes the merge node's address"

                  Expect.equal
                      anonymous.Actor
                      (Actor.ofLegacyString "merge")
                      "the default entry point still stamps the pre-1525 actor"

                  Expect.equal anonymous.PromptId None "…and the pre-1525 (absent) prompt id"
              | other -> failtestf "expected Merged, got %A" other
          }

          // ── 7. The trunk-merge refusal ───────────────────────────────────

          test "mergeIntoTrunkWith hands back the refusal the policy built" {
              // `mergeIntoTrunk` answers `string option`, so a refusal, a missing
              // base, a replay failure and an exhausted retry budget all arrive as
              // `None` — and the refusal, already built, was dropped one line
              // before the caller could act on it.
              let sink = InMemoryDagSink.create<TestMsg> ()
              let initial = buildDashboard ()

              let critical =
                  TreeOp.UpdateStyle(
                      leftChildId,
                      { Defaults.style with
                          Tone = ToneVariant.Critical }
                  )

              let a = stepRecord "s" None success 1L
              add sink a
              let trunk = stepRecord "s" (Some a) brand 2L
              let branch = stepRecord "s" (Some a) critical 3L
              add sink trunk
              add sink branch

              Expect.isTrue (sink.TryAdvanceHead("s", None, trunk.Hash) |> Async.RunSynchronously) "trunk head set"

              let outcome =
                  DagMerge.mergeIntoTrunkWith MergeContext.defaults sink "s" initial branch.Hash (timestamp 9L) 3
                  |> Async.RunSynchronously

              match outcome with
              | TrunkMergeOutcome.Refused(ReconcileFault.Refused refusals) ->
                  Expect.isNonEmpty refusals "the refusal names its contended pairs"

                  Expect.isTrue
                      (refusals |> List.exists (fun r -> r.Conflict.Address = "left"))
                      "the contended left pane is reported"
              | other -> failtestf "expected Refused, got %A" other

              // The lossy face is unchanged — existing callers still see `None`.
              Expect.isNone
                  (DagMerge.mergeIntoTrunk sink "s" initial branch.Hash (timestamp 9L) 3
                   |> Async.RunSynchronously)
                  "the lossy projection still answers None"
          }

          // ── 8. Checkpoint-bounded merge replay ───────────────────────────

          test "a merge replayed FROM a checkpoint reaches the same tree as one replayed from genesis" {
              // The checkpoint is an optimisation, so the property that matters is
              // that it changes nothing: same merged tree, same merge node. A
              // checkpoint that does not bound a head falls back to a full replay,
              // which this fixture also exercises — the checkpoint is taken on the
              // shared base, so it bounds both branch heads but the base replay
              // resolves against it directly.
              let sink = InMemoryDagSink.create<TestMsg> ()
              let initial = buildDashboard ()
              let a = stepRecord "s" None brand 1L
              add sink a
              let branchA = stepRecord "s" (Some a) criticalLeft 2L
              let branchB = stepRecord "s" (Some a) removeRight 3L
              add sink branchA
              add sink branchB

              let dag = sink.Records "s" |> Async.RunSynchronously |> DagOpRecord.toDag

              let checkpoint =
                  match DagFold.checkpointAt initial dag a.Hash with
                  | Ok cp -> cp
                  | Error e -> failtestf "checkpointAt failed: %A" e

              let mergeWith context =
                  DagMerge.mergeWith context sink "s" initial branchA.Hash branchB.Hash (timestamp 9L)
                  |> Async.RunSynchronously

              let fromGenesis = mergeWith MergeContext.defaults

              let fromCheckpoint =
                  mergeWith
                      { MergeContext.defaults<TestMsg> with
                          Checkpoint = Some checkpoint }

              match fromGenesis.Result, fromCheckpoint.Result with
              | MergeResult.Merged(r1, t1), MergeResult.Merged(r2, t2) ->
                  Expect.equal (canonical t2) (canonical t1) "same merged tree"
                  Expect.equal r2.Hash r1.Hash "same merge node — the checkpoint is not in the identity"
              | a, b -> failtestf "expected both Merged, got %A and %A" a b
          }

          test "a merge REFUSES a checkpoint whose state fails its own position-bound seal" {
              // The one case that must NOT fall back to a full replay. A
              // checkpoint that does not bound a head is an optimisation missing
              // its target; a checkpoint whose state does not seal to its
              // claimed position is tamper evidence, and replaying around it would
              // discard the only signal the check exists to raise.
              let sink = InMemoryDagSink.create<TestMsg> ()
              let initial = buildDashboard ()
              let a = stepRecord "s" None brand 1L
              add sink a
              let branchA = stepRecord "s" (Some a) criticalLeft 2L
              let branchB = stepRecord "s" (Some a) removeRight 3L
              add sink branchA
              add sink branchB

              let dag = sink.Records "s" |> Async.RunSynchronously |> DagOpRecord.toDag

              let honest =
                  match DagFold.checkpointAt initial dag a.Hash with
                  | Ok cp -> cp
                  | Error e -> failtestf "checkpointAt failed: %A" e

              // A genuine tree of a DIFFERENT state, presented under the honest
              // checkpoint's seal.
              let tampered =
                  { honest with
                      State = initial |> applyOk branchA.Op }

              let outcome =
                  DagMerge.mergeWith
                      { MergeContext.defaults<TestMsg> with
                          Checkpoint = Some tampered }
                      sink
                      "s"
                      initial
                      branchA.Hash
                      branchB.Hash
                      (timestamp 9L)
                  |> Async.RunSynchronously

              match outcome.Result with
              | MergeResult.ReplayFailed(MergeReplayFault.Checkpoint(Fuaran.Core.Dag.CheckpointBreak.Seal _)) -> ()
              | other -> failtestf "expected ReplayFailed (Checkpoint Seal), got %A" other
          } ]
