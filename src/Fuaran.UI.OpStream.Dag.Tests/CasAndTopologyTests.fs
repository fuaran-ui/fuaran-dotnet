module Fuaran.UI.OpStream.Dag.Tests.CasAndTopologyTests

open Expecto
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Ops.Types
open Fuaran.UI.OpStream.Abstractions
open Fuaran.UI.OpStream.Dag.Abstractions
open Fuaran.UI.OpStream.Dag.InMemory
open Fuaran.UI.OpStream.Dag.Tests.TestSupport

// ============================================================================
//  tryAdvanceHead CAS + DAG topology (parents / reachable / lca).
//
//  A small branch fixture shared by these tests:
//
//        g ── a ── b        (trunk: restyle, then remove-right)
//             └─── c        (branch off `a`: insert a pane)
//
//  `g` is the genesis op; `a` its child; `b` and `c` are two disjoint children
//  of `a` (a branch point). The lca of `b` and `c` is `a`.
// ============================================================================

// ─── Phase 408 — the DAG content hash folds provenance (the F13 hole, closed) ──
// userId / promptId / resultEnvelope are now inside the content address, so
// re-attributing or re-purposing a DAG node changes its id (was undetectable).

[<Tests>]
let provenanceInDagHash =
    testList
        "DAG content address folds provenance (Phase 408)"
        [ test "userId is folded into the content hash" {
              let op = TreeOp.RemoveNode rightChildId
              let a = DagOpRecord.create "s" [] op None "alice" (ts 1L) OpResultEnvelope.Success
              let b = DagOpRecord.create "s" [] op None "mallory" (ts 1L) OpResultEnvelope.Success
              Expect.notEqual a.Hash b.Hash "different userId → different content id"
          }

          test "promptId is folded into the content hash" {
              let op = TreeOp.RemoveNode rightChildId
              let a = DagOpRecord.create "s" [] op None "u" (ts 1L) OpResultEnvelope.Success

              let b =
                  DagOpRecord.create "s" [] op (Some "p-1") "u" (ts 1L) OpResultEnvelope.Success

              Expect.notEqual a.Hash b.Hash "absent vs present promptId → different content id"
          }

          test "resultEnvelope is folded into the content hash" {
              let op = TreeOp.RemoveNode rightChildId
              let ok = DagOpRecord.create "s" [] op None "u" (ts 1L) OpResultEnvelope.Success

              let fail =
                  DagOpRecord.create "s" [] op None "u" (ts 1L) (OpResultEnvelope.Failure("KindMismatch", "boom"))

              Expect.notEqual ok.Hash fail.Hash "flipping Success → Failure → different content id"
          }

          test "recomputeHash catches a re-attributed node (DagVerify tamper-evidence)" {
              let op = TreeOp.RemoveNode rightChildId
              let r = DagOpRecord.create "s" [] op None "alice" (ts 1L) OpResultEnvelope.Success
              // Re-attribute WITHOUT re-hashing — the stored hash no longer recomputes.
              let tampered = { r with UserId = "mallory" }

              Expect.notEqual
                  (DagOpRecord.recomputeHash tampered)
                  tampered.Hash
                  "re-attribution breaks the content address"
          } ]

let private restyle =
    TreeOp.UpdateStyle(
        leftChildId,
        { Defaults.style with
            Tone = ToneVariant.Brand }
    )

let private removeRight = TreeOp.RemoveNode rightChildId

let private insertPane =
    TreeOp.InsertChild(dashboardId, Fuaran.markdown "extra" "Extra")

/// Build the branch fixture into a fresh sink; returns (sink, g, a, b, c).
let private branchFixture () =
    let sink = InMemoryDagSink.create<TestMsg> ()
    let g = stepRecord "s" None restyle 1L
    let a = stepRecord "s" (Some g) removeRight 2L
    let b = stepRecord "s" (Some a) (TreeOp.RemoveNode leftChildId) 3L
    let c = stepRecord "s" (Some a) insertPane 4L
    add sink g
    add sink a
    add sink b
    add sink c
    sink, g, a, b, c

[<Tests>]
let tests =
    testList
        "Dag.CasAndTopology"
        [ test "tryAdvanceHead swaps only when the expected head matches" {
              let sink = InMemoryDagSink.create<TestMsg> ()
              let g = stepRecord "s" None restyle 1L
              let a = stepRecord "s" (Some g) removeRight 2L
              add sink g
              add sink a

              // Genesis advance from the empty state.
              Expect.isTrue (sink.TryAdvanceHead("s", None, g.Hash) |> Async.RunSynchronously) "genesis advance"
              // Stale expected loses.
              Expect.isFalse
                  (sink.TryAdvanceHead("s", None, a.Hash) |> Async.RunSynchronously)
                  "stale expected (None) must lose"
              // Correct expected wins.
              Expect.isTrue
                  (sink.TryAdvanceHead("s", Some g.Hash, a.Hash) |> Async.RunSynchronously)
                  "matching expected advances"

              Expect.equal (sink.Head "s" |> Async.RunSynchronously) (Some a.Hash) "head is now a"
          }

          test "concurrent advancers: exactly one wins the CAS" {
              let sink = InMemoryDagSink.create<TestMsg> ()
              let g = stepRecord "s" None restyle 1L
              let a = stepRecord "s" (Some g) removeRight 2L
              let c = stepRecord "s" (Some g) insertPane 3L
              add sink g
              add sink a
              add sink c
              sink.TryAdvanceHead("s", None, g.Hash) |> Async.RunSynchronously |> ignore

              // Two writers race to advance the trunk from `g`. Run them in
              // parallel; the CAS guarantees exactly one succeeds.
              let results =
                  [ async { return sink.TryAdvanceHead("s", Some g.Hash, a.Hash) |> Async.RunSynchronously }
                    async { return sink.TryAdvanceHead("s", Some g.Hash, c.Hash) |> Async.RunSynchronously } ]
                  |> Async.Parallel
                  |> Async.RunSynchronously

              Expect.equal (results |> Array.filter id |> Array.length) 1 "exactly one advancer wins"
          }

          test "idempotent re-add of an identical record is a no-op" {
              let sink = InMemoryDagSink.create<TestMsg> ()
              let g = stepRecord "s" None restyle 1L
              add sink g
              add sink g // must not throw
              Expect.equal (sink.Records "s" |> Async.RunSynchronously |> List.length) 1 "still one record"
          }

          test "parents query returns author-order parent hashes" {
              let sink, g, a, _, _ = branchFixture ()
              Expect.equal (sink.Parents("s", a.Hash) |> Async.RunSynchronously) [ g.Hash ] "a's parent is g"
              Expect.equal (sink.Parents("s", g.Hash) |> Async.RunSynchronously) [] "g is a genesis"
          }

          test "reachable is the ancestor closure inclusive of self" {
              let sink, g, a, b, _ = branchFixture ()
              let reach = sink.Reachable("s", b.Hash) |> Async.RunSynchronously
              Expect.isTrue (reach.Contains b.Hash) "self is reachable"
              Expect.isTrue (reach.Contains a.Hash) "parent is reachable"
              Expect.isTrue (reach.Contains g.Hash) "grandparent is reachable"
              Expect.equal (Set.count reach) 3 "exactly g,a,b"
          }

          test "lca of two disjoint branches is their branch point" {
              let sink, _, a, b, c = branchFixture ()

              match sink.Lca("s", b.Hash, c.Hash) |> Async.RunSynchronously with
              | LcaResult.Unique h -> Expect.equal h a.Hash "lca(b,c) = a"
              | other -> failtestf "expected Unique a, got %A" other
          }

          test "lca of an ancestor with its descendant is the ancestor (fast-forward shape)" {
              let sink, _, a, b, _ = branchFixture ()

              match sink.Lca("s", a.Hash, b.Hash) |> Async.RunSynchronously with
              | LcaResult.Unique h -> Expect.equal h a.Hash "lca(a,b) = a"
              | other -> failtestf "expected Unique a, got %A" other
          }

          test "heads enumerates the live branch tips" {
              let sink, _, _, b, c = branchFixture ()
              let heads = sink.Heads "s" |> Async.RunSynchronously |> Set.ofList
              Expect.equal heads (Set.ofList [ b.Hash; c.Hash ]) "tips are b and c"
          } ]
