module Fuaran.UI.OpStream.Contract.Tests.ContractTests

open System
open Expecto
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Ops.Types
open Fuaran.UI.OpStream.Abstractions
open Fuaran.UI.OpStream.TestSupport
open Fuaran.UI.OpStream.Contract.Tests.Cells

// ============================================================================
//  The sink contract (Phase 2066).
//
//  Every case runs in every cell — {in-memory, SQLite} x {linear, DAG} — and
//  asserts the same property in each. Where a cell legitimately behaves
//  differently the case says so in one of two words, and nowhere else:
//
//   - INTENDED: the difference is the design, stated by the interface the cell
//     implements (a DAG `Add` is idempotent where a linear `Append` refuses a
//     duplicate; a tombstone keeps the node a linear compaction deletes; an
//     in-memory store does not survive its process).
//
//   - PENDING Phase 1937: a defect that open phase owns. The case asserts
//     TODAY's behaviour, so the phase's landing turns it red and the case is
//     flipped to the shared expectation in the same change — "whichever lands
//     second runs the other's cases". Three are pinned: neither DAG sink checks
//     a record's hash at write; a refused first append leaves a phantom stream
//     in both in-memory sinks; the SQLite sinks keep whole seconds of a
//     timestamp the in-memory ones keep exactly.
// ============================================================================

let private restyle (id: NodeId) (style: SemanticStyle) : TreeOp<TestMsg> = TreeOp.UpdateStyle(id, style)

/// Five ops that apply in order to the two-pane genesis tree, between them
/// carrying a full style, a whole node, a reorder and a removal — the shapes a
/// codec can lose.
let private ops: TreeOp<TestMsg> list =
    [ restyle
          leftChildId
          { Defaults.style with
              Tone = ToneVariant.Brand
              Weight = StyleWeight.Spacious
              Emphasis = Emphasis.Loud }
      TreeOp.InsertChild(dashboardId, Fuaran.markdown "added" "An added pane")
      TreeOp.ReorderChildren(dashboardId, [ NodeId "added"; rightChildId; leftChildId ])
      restyle
          rightChildId
          { Defaults.style with
              Tone = ToneVariant.Success }
      TreeOp.RemoveNode leftChildId ]

let private at (i: int) = timestamp (1_700_000_000L + int64 i)

/// Append the first `n` ops, returning their addresses.
let private appendFirst (h: Harness) (n: int) : string list =
    ops |> List.truncate n |> List.mapi (fun i op -> h.Append (at i) op)

/// The tree the first `n` ops build, folded by the apply engine directly.
let private treeAfter (n: int) : Node<TestMsg> =
    ops
    |> List.truncate n
    |> List.fold (fun tree op -> applyOk op tree) (buildDashboard ())

let private expectTree (actual: Result<Node<TestMsg>, string>) (expected: Node<TestMsg>) (what: string) =
    match actual with
    | Ok tree -> Expect.equal (canonical tree) (canonical expected) what
    | Error e -> failtestf "%s: replay refused: %s" what e

let private refused (outcome: Outcome) (what: string) =
    match outcome with
    | Outcome.Refused _ -> ()
    | Outcome.Accepted -> failtestf "%s: the store accepted it" what

let private accepted (outcome: Outcome) (what: string) =
    match outcome with
    | Outcome.Accepted -> ()
    | Outcome.Refused message -> failtestf "%s: the store refused it: %s" what message

/// Head, trunk and record count, for "the store is exactly as it was".
let private snapshot (h: Harness) = h.Head(), h.Read(), h.RecordCount()

let private contract (name: string) (case: Harness -> unit) : Test =
    testList name [ for cell in cells -> test cell.Name { withHarness cell "contract" case } ]

[<Tests>]
let tests =
    testList
        "Sink contract (Phase 2066)"
        [

          contract "an empty stream has no head and an empty trunk" (fun h ->
              Expect.isNone (h.Head()) "no head before the first append"
              Expect.isEmpty (h.Read()) "no trunk before the first append"
              Expect.equal (h.RecordCount()) 0 "no records before the first append")

          contract "append then read returns every record, in order, field for field" (fun h ->
              let addresses = appendFirst h ops.Length
              let trunk = h.Read()

              Expect.equal (trunk |> List.map _.Position) [ 1 .. ops.Length ] "every record, genesis first"
              Expect.equal (trunk |> List.map _.Address) addresses "each at the address its append returned"

              Expect.equal
                  (trunk |> List.map _.Op)
                  (ops |> List.map CanonicalJson.encodeOp)
                  "each op round-trips the store byte for byte"

              Expect.equal (trunk |> List.map _.Timestamp) (List.init ops.Length at) "each timestamp round-trips"
              Expect.allEqual (trunk |> List.map _.Actor) (Actor.Human "tester") "each actor round-trips"
              Expect.allEqual (trunk |> List.map _.PromptId) None "each prompt id round-trips"
              Expect.isFalse (trunk |> List.exists _.Tombstoned) "nothing is tombstoned"
              Expect.equal (h.Head()) (List.tryLast addresses) "the head is the last append")

          contract "replay from genesis folds the trunk to the tree its ops build" (fun h ->
              appendFirst h ops.Length |> ignore
              expectTree (h.ReplayFromGenesis(buildDashboard ())) (treeAfter ops.Length) "the replayed tree")

          contract "a compare-and-append at the true head appends" (fun h ->
              appendFirst h 2 |> ignore
              Expect.isTrue (h.AppendIf (h.Head()) ops[2]) "the true head admits the append"
              Expect.equal (h.Read() |> List.length) 3 "the trunk grew by one"
              Expect.equal (h.Head()) (h.Read() |> List.tryLast |> Option.map _.Address) "the head advanced")

          contract "a compare-and-append at the empty head of an empty stream appends" (fun h ->
              Expect.isTrue (h.AppendIf None ops[0]) "an empty stream admits a genesis append"
              Expect.equal (h.Read() |> List.length) 1 "the genesis record is the trunk")

          contract "a compare-and-append at a stale head changes neither the head nor the trunk" (fun h ->
              let addresses = appendFirst h 2
              let head, trunk, count = snapshot h

              Expect.isFalse (h.AppendIf (Some addresses[0]) ops[2]) "a stale expectation is refused"
              Expect.isFalse (h.AppendIf None ops[2]) "an empty expectation over a non-empty stream is refused"
              Expect.equal (h.Head()) head "the head did not move"
              Expect.equal (h.Read()) trunk "the trunk is as it was"

              match h.Shape with
              | Linear -> Expect.equal (h.RecordCount()) count "nothing was persisted"
              | Dag ->
                  // INTENDED: a DAG append never contends — only the head does — so the
                  // losing writer's records stay as branches off the nodes it chose.
                  Expect.equal (h.RecordCount()) (count + 2) "the losing writes stay as branches")

          contract "a record whose hash does not recompute is refused at write" (fun h ->
              appendFirst h 2 |> ignore
              let head, trunk, count = snapshot h
              let outcome, forged = h.OfferForgedHash ops[2] ops[3]

              match h.Shape with
              | Linear ->
                  refused outcome "a record whose hash does not recompute"
                  Expect.equal (h.RecordCount()) count "nothing was persisted"
              | Dag ->
                  // PENDING Phase 1937 (`DagWriteAdmission`: `DagVerify.record` at write).
                  // Neither DAG sink recomputes a record's hash when it is added, so the
                  // forged record is stored, and refused only when it is read back: by
                  // address, and by every whole-stream read from then on, the poison pill
                  // `WriteAdmission` exists to prevent on the linear sinks.
                  ignore count
                  accepted outcome "PENDING Phase 1937 - a DAG sink admits a record whose hash does not recompute"
                  refused (h.ReadBack forged) "the read path refuses the forged record"

                  refused
                      (try
                          h.RecordCount() |> ignore
                          Outcome.Accepted
                       with ex ->
                           Outcome.Refused ex.Message)
                      "PENDING Phase 1937 - every whole-stream read now refuses the stream"

              Expect.equal (h.Head()) head "the head did not move"
              Expect.equal (h.Read()) trunk "the trunk is as it was")

          contract "a record linking to a predecessor the store does not hold is refused at write" (fun h ->
              appendFirst h 2 |> ignore
              let before = snapshot h
              refused (h.OfferDanglingLink ops[2]) "a dangling predecessor"
              Expect.equal (snapshot h) before "the store is exactly as it was")

          contract "re-offering the head record leaves the store as it was" (fun h ->
              appendFirst h 2 |> ignore
              let before = snapshot h

              match h.Shape with
              // INTENDED: `IOpStreamSink.Append` refuses a duplicate (StreamId, Sequence).
              | Linear -> refused (h.ReofferHead()) "a linear sink refuses the duplicate"
              // INTENDED: `IDagOpStreamSink.Add` is idempotent on an identical record.
              | Dag -> accepted (h.ReofferHead()) "a DAG sink takes the identical record as a no-op"

              Expect.equal (snapshot h) before "the store is exactly as it was")

          contract "compaction keeps the head, the records above it, and the next append" (fun h ->
              appendFirst h 4 |> ignore
              let head = h.Head()
              let trunk = h.Read()
              h.Compact 2
              let compacted = h.Read()

              Expect.equal (h.Head()) head "the head did not move"

              Expect.equal
                  (compacted |> List.filter (fun e -> e.Position > 2))
                  (trunk |> List.filter (fun e -> e.Position > 2))
                  "the records above the compaction point read back unchanged"

              match h.Shape with
              // INTENDED: a linear compaction deletes the ops it covers.
              | Linear ->
                  Expect.isFalse (compacted |> List.exists (fun e -> e.Position <= 2)) "the covered ops are gone"
              // INTENDED: a tombstone drops the payload and keeps the hash and the
              // parents, so the chain still links and verifies.
              | Dag ->
                  let covered = compacted |> List.filter (fun e -> e.Position <= 2)

                  Expect.equal
                      (covered |> List.map _.Address)
                      (trunk |> List.take 2 |> List.map _.Address)
                      "the covered nodes keep their addresses"

                  Expect.isTrue (covered |> List.forall _.Tombstoned) "the covered nodes are tombstoned"

              let next = h.Append (at 9) ops[4]
              Expect.equal (h.Head()) (Some next) "the next append extends the compacted stream"
              Expect.equal (h.Read() |> List.last |> _.Position) 5 "at the position after the old head")

          contract "replay from a checkpoint reaches the tree replay from genesis reaches" (fun h ->
              appendFirst h 3 |> ignore
              h.Checkpoint(treeAfter 3)
              Expect.isTrue (h.CheckpointVerifies()) "the checkpoint, as the store holds it, verifies"
              h.Append (at 3) ops[3] |> ignore
              h.Append (at 4) ops[4] |> ignore
              expectTree (h.ReplayFromGenesis(buildDashboard ())) (treeAfter 5) "replay from genesis"
              expectTree (h.ReplayFromCheckpoint(buildDashboard ())) (treeAfter 5) "replay from the checkpoint")

          contract "replay from the retained checkpoint survives compaction through it" (fun h ->
              appendFirst h 3 |> ignore
              h.Checkpoint(treeAfter 3)
              h.Append (at 3) ops[3] |> ignore
              h.Append (at 4) ops[4] |> ignore
              h.Compact 3
              Expect.isTrue (h.CheckpointVerifies()) "the retained checkpoint still verifies"
              expectTree (h.ReplayFromCheckpoint(buildDashboard ())) (treeAfter 5) "replay from the checkpoint")

          contract "a refused first append leaves no stream behind" (fun h ->
              refused (h.OfferDanglingLink ops[0]) "a dangling genesis record"
              let listed = h.Streams() |> List.contains h.StreamId

              match h.Store with
              | InMemory ->
                  // PENDING Phase 1937 (`appendLocked` admits before creating the stream).
                  // Both in-memory sinks create the stream's state, then refuse the
                  // record, so `Streams` lists a stream that holds nothing. The task names
                  // the linear `appendLocked`; `InMemoryDagSink.Add` calls `getOrCreate`
                  // before its parent-presence check, the same defect in the DAG sink.
                  Expect.isTrue listed "PENDING Phase 1937 - an in-memory sink lists a phantom stream"
              | Sqlite -> Expect.isFalse listed "no stream is listed for a refused append")

          contract "a sub-second timestamp reads back as it was written" (fun h ->
              let written = DateTimeOffset(2026, 10, 12, 9, 30, 15, 250, TimeSpan.Zero)
              h.Append written ops[0] |> ignore
              let read = (h.Read() |> List.exactlyOne).Timestamp

              match h.Store with
              | InMemory -> Expect.equal read written "the in-memory store keeps the timestamp exactly"
              | Sqlite ->
                  // PENDING Phase 1937 ("backends agree on timestamp precision"): the
                  // SQLite sinks store unix seconds. The chain hash covers whole seconds,
                  // so the record still verifies, but it reads back different by backend.
                  Expect.equal
                      read
                      (DateTimeOffset.FromUnixTimeSeconds(written.ToUnixTimeSeconds()))
                      "PENDING Phase 1937 - a SQLite sink keeps whole seconds")

          contract "a store reopened over the same storage reads back the same stream" (fun h ->
              let addresses = appendFirst h 3

              match h.Reopen with
              | None ->
                  // INTENDED: an in-memory store lives as long as its process.
                  Expect.equal h.Store InMemory "only an in-memory store has nothing to reopen"
              | Some reopen ->
                  let again = reopen ()
                  Expect.equal (again.Read()) (h.Read()) "the trunk reads back the same"
                  Expect.equal (again.Head()) (List.tryLast addresses) "the head reads back the same"
                  let next = again.Append (at 3) ops[3]
                  Expect.equal (again.Head()) (Some next) "the reopened store takes the next append"
                  Expect.equal (h.Head()) (Some next) "and the first sink sees it") ]
