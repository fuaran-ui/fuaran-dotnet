module Fuaran.UI.OpStream.Tests.VerifyOnReadTests

open System
open System.IO
open Expecto
open Microsoft.Data.Sqlite
open Fuaran.UI.Types
open Fuaran.UI.Ops.Types
open Fuaran.UI.OpStream.Abstractions
open Fuaran.UI.OpStream.InMemory
open Fuaran.UI.OpStream.Sqlite
open Fuaran.UI.OpStream.Replay
open Fuaran.UI.OpStream.Tests.TestSupport

// ============================================================================
//  Phase 793 — the chain is verified on the READ paths.
//
//  Every test here is a GO-RED proof: it corrupts a real record in a real
//  store and asserts the read path refuses it BY NAME. A verification that
//  cannot fail is the exact defect this phase exists to remove, so each check
//  is shown failing on genuine corruption before it is believed when it
//  passes. The Sqlite cases corrupt the DATABASE FILE with raw SQL — not a
//  record in memory — so what is proved is that a store changed underneath
//  the process is caught, which is the whole point of verifying on read
//  rather than trusting what we wrote.
//
//  What is being proved is corruption detection, not tamper evidence: the
//  chain is unkeyed, so a writer who edits a record and re-chains from there
//  passes every check below. See CRYPTO.md.
// ============================================================================

let private removeRight = TreeOp.RemoveNode(NodeId "right"): TreeOp<TestMsg>
let private removeLeft = TreeOp.RemoveNode(NodeId "left"): TreeOp<TestMsg>

/// A record whose stored `Hash` no longer matches its content: the timestamp
/// moves, the hash does not. This is precisely the shape a bit-flip, a torn
/// write, or a hand-run `UPDATE` produces.
let private corruptContent (r: OpRecord<TestMsg>) : OpRecord<TestMsg> =
    { r with
        Timestamp = r.Timestamp.AddSeconds 1.0 }

/// Three properly-chained records over one stream.
let private cleanChain (streamId: string) =
    let r1 = buildRecord streamId 1 removeRight None (timestamp 100L)
    let r2 = buildRecord streamId 2 removeLeft (Some r1) (timestamp 200L)

    let r3 =
        buildRecord streamId 3 (TreeOp.RemoveNode(NodeId "dash")) (Some r2) (timestamp 300L)

    r1, r2, r3

/// Run `read` expecting it to REFUSE, and hand back the refusal message so the
/// test can assert the record was named. A read that succeeds fails the test —
/// that is the go-red half, and it is the half worth writing.
let private refusalMessage (what: string) (read: unit -> unit) : string =
    try
        read ()
        failtestf "GO-RED FAILED: %s returned a corrupt segment instead of refusing" what
    with :? InvalidOperationException as ex ->
        ex.Message

let private freshDbPath () : string =
    Path.Combine(Path.GetTempPath(), sprintf "fuaran-verify-%s.db" (Guid.NewGuid().ToString("N")))

let private cleanup (path: string) =
    try
        if File.Exists path then
            File.Delete path
    with _ ->
        ()

/// Mutate the persisted DB directly — the corruption a verifying read exists
/// to catch. Bumping `timestamp` leaves the row perfectly well-formed and
/// perfectly decodable; only the recomputed hash disagrees.
let private corruptRowInDb (path: string) (streamId: string) (sequence: int) =
    use conn = new SqliteConnection(sprintf "Data Source=%s" path)
    conn.Open()
    use cmd = conn.CreateCommand()

    cmd.CommandText <- "UPDATE op_stream SET timestamp = timestamp + 1 WHERE stream_id = @s AND sequence = @q;"

    cmd.Parameters.AddWithValue("@s", streamId) |> ignore
    cmd.Parameters.AddWithValue("@q", sequence) |> ignore
    let affected = cmd.ExecuteNonQuery()

    if affected <> 1 then
        failtestf "corruptRowInDb: expected to corrupt exactly 1 row, touched %d" affected

[<Tests>]
let tests =
    testList
        "Fuaran.UI.OpStream — verify on read (Phase 793)"
        [

          // ── Verify.segmentFrom / segment ────────────────────────────────

          test "segment accepts a clean whole stream" {
              let r1, r2, r3 = cleanChain "seg-ok"

              match Verify.segment [ r1; r2; r3 ] with
              | Ok() -> ()
              | Error e -> failtestf "Expected a clean segment, got %A" e
          }

          test "segment accepts a MID-STREAM slice that chain would reject" {
              let _, r2, r3 = cleanChain "seg-mid"

              // The genesis-anchored verifier reports this as out-of-order — it is
              // asking a different question, and that is exactly why `chain` alone
              // could not be wired into any read path.
              match Verify.chain [ r2; r3 ] with
              | Ok() -> failtest "Expected `chain` to reject a slice that does not start at genesis"
              | Error(VerificationError.OutOfOrder(expected, actual)) ->
                  Expect.equal expected 1 "chain expects the segment to start at 1"
                  Expect.equal actual 2 "the slice actually starts at 2"
              | Error other -> failtestf "Expected OutOfOrder from `chain`, got %A" other

              match Verify.segment [ r2; r3 ] with
              | Ok() -> ()
              | Error e -> failtestf "Expected the anchor-relative verifier to accept the slice, got %A" e
          }

          test "segment REJECTS a corrupt record in a mid-stream slice, at its ABSOLUTE sequence" {
              let _, r2, r3 = cleanChain "seg-mid-bad"

              match Verify.segment [ r2; corruptContent r3 ] with
              | Ok() -> failtest "GO-RED FAILED: a corrupt record in a slice verified clean"
              | Error(VerificationError.HashMismatch(sequence, _, _)) ->
                  // 3, not 2 — the report is in the stream's own numbering, not the
                  // slice's. A caller cannot act on a slice-relative index.
                  Expect.equal sequence 3 "names the corrupt record by its absolute sequence"
              | Error other -> failtestf "Expected HashMismatch at 3, got %A" other
          }

          test "segment rejects a sequence GAP (the shape a truncation leaves)" {
              let r1, _, r3 = cleanChain "seg-gap"

              match Verify.segment [ r1; r3 ] with
              | Ok() -> failtest "GO-RED FAILED: a stream missing record 2 verified clean"
              | Error(VerificationError.OutOfOrder(expected, actual)) ->
                  Expect.equal expected 2 "expected the missing record's sequence"
                  Expect.equal actual 3 "found the record that followed it"
              | Error other -> failtestf "Expected OutOfOrder, got %A" other
          }

          test "segment demands the genesis anchor when the slice starts at sequence 1" {
              let r1, r2, _ = cleanChain "seg-genesis"

              let relabelled =
                  { r1 with
                      PreviousHash = String.replicate 64 "a" }

              match Verify.segment [ relabelled; r2 ] with
              | Ok() -> failtest "GO-RED FAILED: a sequence-1 record with a non-genesis anchor verified clean"
              | Error(VerificationError.PreviousHashMismatch(sequence, _, _)) ->
                  Expect.equal sequence 1 "names the genesis record"
              | Error other -> failtestf "Expected PreviousHashMismatch at 1, got %A" other
          }

          test "segmentFrom binds a tail to an anchor the caller already trusts" {
              let r1, r2, r3 = cleanChain "seg-from"

              match Verify.segmentFrom r1.Hash 2 [ r2; r3 ] with
              | Ok() -> ()
              | Error e -> failtestf "Expected the tail to verify against its true anchor, got %A" e

              // The strictly-stronger property `segment` cannot offer: a tail
              // presented at the WRONG position is caught.
              match Verify.segmentFrom (String.replicate 64 "b") 2 [ r2; r3 ] with
              | Ok() -> failtest "GO-RED FAILED: a tail verified against an anchor it does not link to"
              | Error(VerificationError.PreviousHashMismatch(sequence, _, _)) ->
                  Expect.equal sequence 2 "names the first record of the tail"
              | Error other -> failtestf "Expected PreviousHashMismatch at 2, got %A" other
          }

          // ── Replay ──────────────────────────────────────────────────────

          test "Replay.applyTo REFUSES a corrupt chain, naming the first bad record" {
              let tree = buildDashboard ()
              let r1, r2, _ = cleanChain "replay-bad"

              match Replay.applyTo tree [ r1; corruptContent r2 ] with
              | Ok _ -> failtest "GO-RED FAILED: replay folded a corrupt chain into a tree"
              | Error(ReplayError.ChainBroken(VerificationError.HashMismatch(sequence, _, _))) ->
                  Expect.equal sequence 2 "names the first bad record"
              | Error other -> failtestf "Expected ChainBroken/HashMismatch at 2, got %A" other
          }

          test "Replay refuses the WHOLE stream on a break — the clean prefix is not applied" {
              // The documented post-break decision. r1 removes 'right' and would
              // have applied cleanly; the result must show it did not.
              let tree = buildDashboard ()
              let r1, r2, _ = cleanChain "replay-whole"

              match Replay.applyTo tree [ r1; corruptContent r2 ] with
              | Ok _ -> failtest "GO-RED FAILED: replay returned a tree from a corrupt stream"
              | Error(ReplayError.ChainBroken _) ->
                  // And the escape hatch is exactly one call away, for a caller
                  // that decides — visibly — that it wants the prefix.
                  match Replay.applyToUnverified tree [ r1 ] with
                  | Ok prefixTree ->
                      match prefixTree.Kind with
                      | NodeKind.Box spec ->
                          Expect.equal (spec.Children |> List.map _.Id) [ "left" ] "the prefix WOULD have applied"
                      | other -> failtestf "Expected a dashboard, got %A" other
                  | Error e -> failtestf "Expected the prefix to apply, got %A" e
              | Error other -> failtestf "Expected ChainBroken, got %A" other
          }

          test "Replay.applyTo accepts a post-checkpoint tail that starts above sequence 1" {
              let tree = buildDashboard ()
              let _, r2, r3 = cleanChain "replay-tail"

              // The regression this guards: verifying with the genesis-anchored
              // `chain` here would refuse every checkpointed resume in the estate.
              match Replay.applyTo tree [ r2; r3 ] with
              | Ok _ -> ()
              | Error(ReplayError.ApplyFailed _) -> () // apply semantics are not this test's subject
              | Error(ReplayError.ChainBroken e) -> failtestf "A legitimate tail was refused as corrupt: %A" e
          }

          // ── InMemorySink ────────────────────────────────────────────────

          test "InMemorySink.Replay REFUSES a corrupt record it is holding" {
              let sink = InMemorySink.create<TestMsg> ()
              let r1, r2, _ = cleanChain "mem-bad"

              sink.Append r1 |> Async.RunSynchronously
              sink.Append(corruptContent r2) |> Async.RunSynchronously

              let message =
                  refusalMessage "InMemorySink.Replay" (fun () ->
                      sink.Replay("mem-bad", 1, 10) |> Async.RunSynchronously |> ignore)

              Expect.stringContains message "mem-bad" "names the stream"
              Expect.stringContains message "sequence 2" "names the first bad record"
          }

          test "InMemorySink.Replay accepts a clean segment" {
              let sink = InMemorySink.create<TestMsg> ()
              let r1, r2, r3 = cleanChain "mem-ok"

              for r in [ r1; r2; r3 ] do
                  sink.Append r |> Async.RunSynchronously

              let replayed = sink.Replay("mem-ok", 1, 10) |> Async.RunSynchronously
              Expect.equal replayed.Length 3 "all three records returned"
          }

          test "LoadVerification.Off hands the corrupt segment back — the opt-out is real, and named" {
              let sink = InMemorySink.createWith<TestMsg> LoadVerification.Off
              let r1, r2, _ = cleanChain "mem-off"

              sink.Append r1 |> Async.RunSynchronously
              sink.Append(corruptContent r2) |> Async.RunSynchronously

              let replayed = sink.Replay("mem-off", 1, 10) |> Async.RunSynchronously
              Expect.equal replayed.Length 2 "Off returns the segment unchecked"
          }

          test "LoadVerification.Tail checks its window and states plainly what it misses" {
              let r1, r2, r3 = cleanChain "mem-tail"

              // Corruption INSIDE the window is caught.
              let inside = InMemorySink.createWith<TestMsg> (LoadVerification.Tail 2)
              inside.Append r1 |> Async.RunSynchronously
              inside.Append r2 |> Async.RunSynchronously
              inside.Append(corruptContent r3) |> Async.RunSynchronously

              refusalMessage "LoadVerification.Tail 2" (fun () ->
                  inside.Replay("mem-tail", 1, 10) |> Async.RunSynchronously |> ignore)
              |> ignore

              // Corruption BEFORE the window is NOT caught. This is the honest cost
              // of the fast path, asserted rather than described, so that anyone who
              // widens the default has to come here and change a test that says so.
              let outside = InMemorySink.createWith<TestMsg> (LoadVerification.Tail 1)
              outside.Append r1 |> Async.RunSynchronously
              outside.Append(corruptContent r2) |> Async.RunSynchronously
              outside.Append r3 |> Async.RunSynchronously

              let replayed = outside.Replay("mem-tail", 1, 10) |> Async.RunSynchronously
              Expect.equal replayed.Length 3 "Tail 1 does not look before its window"
          }

          // ── SqliteSink — corruption of the DATABASE FILE ────────────────

          test "SqliteSink.Replay REFUSES a row corrupted in the database file" {
              let path = freshDbPath ()

              try
                  let write: IOpStreamSink<TestMsg> =
                      SqliteSink.create (sprintf "Data Source=%s" path) testCodec

                  let r1, r2, r3 = cleanChain "sql-bad"

                  for r in [ r1; r2; r3 ] do
                      write.Append r |> Async.RunSynchronously

                  // Clean before, so the refusal below is attributable to the
                  // corruption and not to a defect in the fixture.
                  Expect.equal
                      (write.Replay("sql-bad", 1, 10) |> Async.RunSynchronously).Length
                      3
                      "the store verifies clean before corruption"

                  corruptRowInDb path "sql-bad" 2

                  let reopened: IOpStreamSink<TestMsg> =
                      SqliteSink.create (sprintf "Data Source=%s" path) testCodec

                  let message =
                      refusalMessage "SqliteSink.Replay" (fun () ->
                          reopened.Replay("sql-bad", 1, 10) |> Async.RunSynchronously |> ignore)

                  Expect.stringContains message "sql-bad" "names the stream"
                  Expect.stringContains message "sequence 2" "names the first bad record"
              finally
                  cleanup path
          }

          test "SqliteSink under LoadVerification.Off returns the corrupted rows" {
              let path = freshDbPath ()

              try
                  let write: IOpStreamSink<TestMsg> =
                      SqliteSink.create (sprintf "Data Source=%s" path) testCodec

                  let r1, r2, _ = cleanChain "sql-off"

                  for r in [ r1; r2 ] do
                      write.Append r |> Async.RunSynchronously

                  corruptRowInDb path "sql-off" 2

                  let unchecked': IOpStreamSink<TestMsg> =
                      SqliteSink.createWith LoadVerification.Off (sprintf "Data Source=%s" path) testCodec

                  let replayed = unchecked'.Replay("sql-off", 1, 10) |> Async.RunSynchronously
                  Expect.equal replayed.Length 2 "Off returns the rows unchecked"
              finally
                  cleanup path
          } ]
