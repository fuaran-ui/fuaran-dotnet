module Fuaran.UI.OpStream.Dag.Tests.MergeTests

open Expecto
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Ops.Types
open Fuaran.UI.OpStream.Abstractions
open Fuaran.UI.OpStream.Dag.Abstractions
open Fuaran.UI.OpStream.Dag.InMemory
open Fuaran.UI.OpStream.Dag.Merge
open Fuaran.UI.OpStream.Dag.Tests.TestSupport

// ============================================================================
//  M1 merge — fast-forward + disjoint auto-merge + conflict refusal + the
//  NodeId-byte tie-break determinism (Phase 178 acceptance #2 + #3 F#-side).
//
//  Each test seeds a base op `a` off the genesis dashboard, then branches off
//  `a`. `now` is a fixed timestamp (no wall-clock in the merge identity beyond
//  this constant).
// ============================================================================

let private brand =
    TreeOp.UpdateStyle(
        leftChildId,
        { Defaults.style with
            Tone = ToneVariant.Brand }
    )

let private critical =
    TreeOp.UpdateStyle(
        leftChildId,
        { Defaults.style with
            Tone = ToneVariant.Critical }
    )

let private restyleRight =
    TreeOp.UpdateStyle(
        rightChildId,
        { Defaults.style with
            Tone = ToneVariant.Success }
    )

let private now = ts 5_000L

/// Build a fresh sink seeded with a single base op `a` off the genesis tree;
/// returns (sink, initial, a).
let private seedBase (baseOp: TreeOp<TestMsg>) =
    let sink = InMemoryDagSink.create<TestMsg> ()
    let initial = buildDashboard ()
    let a = stepRecord "s" None baseOp 1L
    add sink a
    sink, initial, a

let private mergeNow (sink: IDagOpStreamSink<TestMsg>) initial (ha: string) (hb: string) =
    DagMerge.merge recordAuthor sink "s" initial ha hb now |> Async.RunSynchronously

[<Tests>]
let tests =
    testList
        "Dag.Merge"
        [ test "identical heads are AlreadyMerged" {
              let sink, initial, a = seedBase brand

              match mergeNow sink initial a.Hash a.Hash with
              | MergeResult.AlreadyMerged h -> Expect.equal h a.Hash "head returned"
              | other -> failtestf "expected AlreadyMerged, got %A" other
          }

          test "ancestor + descendant fast-forwards to the descendant" {
              let sink, initial, a = seedBase brand
              let b = stepRecord "s" (Some a) restyleRight 2L
              add sink b

              match mergeNow sink initial a.Hash b.Hash with
              | MergeResult.FastForward h -> Expect.equal h b.Hash "ff to descendant b"
              | other -> failtestf "expected FastForward b, got %A" other
          }

          test "disjoint edits to different nodes auto-merge with zero conflicts" {
              let sink, initial, a = seedBase brand
              // A restyles the right pane; B restyles the left pane — disjoint
              // content cells (right vs left).
              let branchA = stepRecord "s" (Some a) restyleRight 2L
              let branchB = stepRecord "s" (Some a) critical 3L
              add sink branchA
              add sink branchB

              match mergeNow sink initial branchA.Hash branchB.Hash with
              | MergeResult.Merged(record, tree) ->
                  Expect.equal (List.length record.Parents) 2 "merge node has two parents"
                  Expect.equal record.Parents [ branchA.Hash; branchB.Hash ] "primary parent first"
                  Expect.isSome record.OutcomeHash "merge node carries an outcome hash"
                  // Expected: base (brand left) + right Success + left Critical.
                  let expected =
                      buildDashboard () |> applyOk a.Op |> applyOk restyleRight |> applyOk critical

                  Expect.equal (canonical tree) (canonical expected) "merged tree has both edits"
                  // Replaying the committed merge node reconstructs the merged tree.
                  add sink record
                  let replayed = replaySpine sink "s" initial record.Hash
                  Expect.equal (canonical replayed) (canonical tree) "merge node replays to merged tree"
              | other -> failtestf "expected Merged, got %A" other
          }

          test "overlapping edits to the same node refuse with the contended cell" {
              let sink = InMemoryDagSink.create<TestMsg> ()
              let initial = buildDashboard ()
              let a = stepRecord "s" None restyleRight 1L
              add sink a
              let branchA = stepRecord "s" (Some a) brand 2L
              let branchB = stepRecord "s" (Some a) critical 3L
              add sink branchA
              add sink branchB

              match mergeNow sink initial branchA.Hash branchB.Hash with
              | MergeResult.NeedsManualMerge cells ->
                  let leftTone =
                      cells |> List.tryFind (fun c -> c.NodeId = "left" && c.Facet = "style.tone")

                  Expect.isSome leftTone "contended (left, style.tone) cell named"

                  match leftTone with
                  | Some c -> Expect.equal c.Class MergeConflictClass.ConcurrentEdit "ConcurrentEdit class"
                  | None -> ()
              | other -> failtestf "expected NeedsManualMerge, got %A" other
          }

          test "disjoint structural inserts auto-merge with a NodeId-byte tie-break, order-independent" {
              let mkSink () =
                  let sink = InMemoryDagSink.create<TestMsg> ()
                  let a = stepRecord "s" None brand 1L
                  add sink a
                  let insZ = TreeOp.InsertChild(dashboardId, Fuaran.markdown "zzz" "Z pane")
                  let insA = TreeOp.InsertChild(dashboardId, Fuaran.markdown "aaa" "A pane")
                  let branchA = stepRecord "s" (Some a) insZ 2L
                  let branchB = stepRecord "s" (Some a) insA 3L
                  add sink branchA
                  add sink branchB
                  sink, branchA, branchB

              let initial = buildDashboard ()
              let s1, ba1, bb1 = mkSink ()
              let s2, ba2, bb2 = mkSink ()

              let r1 = mergeNow s1 initial ba1.Hash bb1.Hash
              // Swap the merge order on the second sink.
              let r2 = mergeNow s2 initial bb2.Hash ba2.Hash

              match r1, r2 with
              | MergeResult.Merged(rec1, tree1), MergeResult.Merged(rec2, tree2) ->
                  // Children order: base survivors [left,right] then new ids
                  // sorted by NodeId bytes [aaa,zzz].
                  let ids =
                      match tree1.Kind with
                      | NodeKind.Box(spec) -> spec.Children |> List.map (fun c -> let s = c.Id in s)
                      | _ -> []

                  Expect.equal
                      ids
                      [ "left"; "right"; "aaa"; "zzz" ]
                      "new children sorted by NodeId bytes after survivors"

                  Expect.equal (canonical tree1) (canonical tree2) "merge is order-independent"
                  Expect.equal rec1.OutcomeHash rec2.OutcomeHash "outcome hash is order-independent"
              | _ -> failtestf "expected both Merged, got %A / %A" r1 r2
          }

          test "criss-cross history (ambiguous LCA) resolves via recursive-base merge, order-independent" {
              let mk () =
                  let sink = InMemoryDagSink.create<TestMsg> ()
                  let x = stepRecord "s" None brand 1L
                  let p = stepRecord "s" (Some x) restyleRight 2L
                  let q = stepRecord "s" (Some x) (TreeOp.RemoveNode rightChildId) 3L
                  add sink x
                  add sink p
                  add sink q
                  // Two merge-shaped nodes each parented on {p,q} → lca is
                  // ambiguous ({p,q} are both maximal common ancestors).
                  let h1 =
                      DagOpRecord.create
                          "s"
                          [ p.Hash; q.Hash ]
                          brand
                          None
                          (Actor.Human "t")
                          (ts 4L)
                          OpResultEnvelope.Success

                  let h2 =
                      DagOpRecord.create
                          "s"
                          [ q.Hash; p.Hash ]
                          critical
                          None
                          (Actor.Human "t")
                          (ts 5L)
                          OpResultEnvelope.Success

                  add sink h1
                  add sink h2
                  sink, h1, h2

              let initial = buildDashboard ()
              let s1, h1a, h2a = mk ()
              let s2, h1b, h2b = mk ()

              // M2: criss-cross is RESOLVED by recursive-base merge (not deferred,
              // which was the M1 boundary).
              match mergeNow s1 initial h1a.Hash h2a.Hash, mergeNow s2 initial h2b.Hash h1b.Hash with
              | MergeResult.Merged(rec1, _), MergeResult.Merged(rec2, _) ->
                  Expect.equal rec1.OutcomeHash rec2.OutcomeHash "recursive-base merge is order-independent"
              | other -> failtestf "expected both Merged (recursive-base resolved), got %A" other
          }

          test "two disjoint roots have no common base" {
              let sink = InMemoryDagSink.create<TestMsg> ()
              let g1 = stepRecord "s" None brand 1L
              let g2 = stepRecord "s" None critical 2L
              add sink g1
              add sink g2
              let initial = buildDashboard ()

              match mergeNow sink initial g1.Hash g2.Hash with
              | MergeResult.NoCommonBase -> ()
              | other -> failtestf "expected NoCommonBase, got %A" other
          }

          // ── Phase 1497: the two-sided envelope + the shared-children guard ──
          //
          // Both repairs were MEASURED by Phase 1488's Fable law harness and left open there,
          // because both change a shipped package's public conflict contract. The harness states
          // them over generated edits on two pipelines; these state them on the shipped types,
          // where a wrong FIELD (rather than a wrong count) is visible.

          test "a refusal records BOTH sides' values with no primacy pin held" {
              let baseTree = buildDashboard ()
              let a = baseTree |> applyOk brand
              let b = baseTree |> applyOk critical

              match TreeMerge.merge3Way baseTree a b with
              | Ok _ -> failtest "expected a refusal on the contended left tone"
              | Error conflicts ->
                  let c =
                      conflicts |> List.find (fun c -> c.NodeId = "left" && c.Facet = "style.tone")

                  Expect.isFalse c.PrimacyHeld "two secondary sides hold no pin"
                  Expect.isSome c.A "the A-side value is recorded"
                  Expect.isSome c.B "the B-side value is recorded"

                  Expect.notEqual
                      (c.A |> Option.map _.Value)
                      (c.B |> Option.map _.Value)
                      "the two sides wanted different things"
                  // The precedence slots are a precedence CLAIM, so with no pin they say nothing.
                  // Before 1497 `Secondary` carried whichever branch arrived first.
                  Expect.isNone c.Primary "no pin held: no primary claim"
                  Expect.isNone c.Secondary "no pin held: no secondary claim"
          }

          test "swapping the branches TRANSPOSES a refusal envelope and changes nothing else" {
              let baseTree = buildDashboard ()
              let a = baseTree |> applyOk brand
              let b = baseTree |> applyOk critical

              match TreeMerge.merge3Way baseTree a b, TreeMerge.merge3Way baseTree b a with
              | Error forward, Error reverse ->
                  let f = MergeConflict.sortCanonical forward
                  let r = MergeConflict.sortCanonical reverse

                  Expect.equal (List.length f) (List.length r) "the same number of contended cells"

                  for (fc, rc) in List.zip f r do
                      Expect.equal (fc.NodeId, fc.Facet) (rc.NodeId, rc.Facet) "the same cell"
                      Expect.equal fc.Class rc.Class "the same class"
                      Expect.equal fc.Base rc.Base "the same base value"
                      Expect.equal fc.A rc.B "the forward A side is the swapped B side"
                      Expect.equal fc.B rc.A "the forward B side is the swapped A side"
                      Expect.equal fc.PrimacyHeld rc.PrimacyHeld "the same pin verdict"
              | other -> failtestf "expected both orders to refuse, got %A" other
          }

          test "merge3Way base a a returns a, even when a changed a node's children" {
              let baseTree = buildDashboard ()
              // A pure structural branch — the case that refused outright before Phase 1497 (223 of
              // 300 trials in the 1488 harness), because the children facet had no
              // "both sides changed it identically" guard.
              let a =
                  baseTree
                  |> applyOk (TreeOp.InsertChild(dashboardId, Fuaran.markdown "zzz" "Z pane"))
                  |> applyOk brand

              match TreeMerge.merge3Way baseTree a a with
              | Ok merged -> Expect.equal (canonical merged) (canonical a) "self-merge is the identity"
              | Error conflicts -> failtestf "self-merge refused: %A" conflicts
          }

          test "two branches inserting the same id with DIFFERENT content refuse, naming the id" {
              let baseTree = buildDashboard ()

              let a =
                  baseTree
                  |> applyOk (TreeOp.InsertChild(dashboardId, Fuaran.markdown "new" "A wrote this"))

              let b =
                  baseTree
                  |> applyOk (TreeOp.InsertChild(dashboardId, Fuaran.markdown "new" "B wrote this"))

              // The shared-children guard reaches this case; without the content check beneath it
              // `recurseChild` would take the A side unconditionally and the merged tree would
              // depend on the argument order.
              match TreeMerge.merge3Way baseTree a b with
              | Ok merged -> failtestf "expected a refusal, got a merged tree: %s" (canonical merged)
              | Error conflicts ->
                  let c = conflicts |> List.find (fun c -> c.NodeId = "new")
                  Expect.equal c.Facet "insert" "the contended cell is the insert itself"
                  Expect.equal c.Class MergeConflictClass.ConcurrentEdit "ConcurrentEdit class"
                  Expect.equal c.Base "" "the id has no base value — it exists on neither side of the LCA"
                  Expect.isSome c.A "the A-side inserted node is recorded"
                  Expect.isSome c.B "the B-side inserted node is recorded"
          }

          test "two branches inserting the same id with the SAME content take the shared value" {
              let baseTree = buildDashboard ()
              let ins = TreeOp.InsertChild(dashboardId, Fuaran.markdown "new" "agreed")
              let a = baseTree |> applyOk ins
              let b = baseTree |> applyOk ins

              match TreeMerge.merge3Way baseTree a b with
              | Ok merged ->
                  let ids =
                      match merged.Kind with
                      | NodeKind.Box(spec) -> spec.Children |> List.map _.Id
                      | _ -> []

                  Expect.equal ids [ "left"; "right"; "new" ] "the shared child appears exactly once"
              | Error conflicts -> failtestf "agreeing inserts refused: %A" conflicts
          }

          test "the refusal envelope encodes to byte-stable canonical JSON" {
              let baseTree = buildDashboard ()
              let a = baseTree |> applyOk brand
              let b = baseTree |> applyOk critical

              match TreeMerge.merge3Way baseTree a b, TreeMerge.merge3Way baseTree a b with
              | Error one, Error two ->
                  let encoded = MergeConflict.encodeEnvelope one
                  Expect.equal encoded (MergeConflict.encodeEnvelope two) "the encoding is stable"
                  Expect.stringStarts encoded "[{\"a\":" "keys are emitted in alphabetical order"
                  Expect.stringContains encoded "\"primacyHeld\":false" "the precedence verdict is projected"
              | other -> failtestf "expected refusals, got %A" other
          }

          test "commitMerge adds the node and advances the trunk under CAS" {
              let sink = InMemoryDagSink.create<TestMsg> ()
              let initial = buildDashboard ()
              let a = stepRecord "s" None brand 1L
              add sink a
              sink.TryAdvanceHead("s", None, a.Hash) |> Async.RunSynchronously |> ignore
              let branchA = stepRecord "s" (Some a) restyleRight 2L
              let branchB = stepRecord "s" (Some a) critical 3L
              add sink branchA
              add sink branchB

              match mergeNow sink initial branchA.Hash branchB.Hash with
              | MergeResult.Merged(record, tree) ->
                  // Trunk is at `a`; commit the merge with expected = a.
                  let committed =
                      DagMerge.commitMerge sink "s" (Some a.Hash) record |> Async.RunSynchronously

                  Expect.isTrue committed "CAS commit succeeds when expected head matches"

                  Expect.equal
                      (sink.Head "s" |> Async.RunSynchronously)
                      (Some record.Hash)
                      "trunk advanced to merge node"

                  let replayed = replaySpine sink "s" initial record.Hash
                  Expect.equal (canonical replayed) (canonical tree) "trunk head replays to merged tree"
              | other -> failtestf "expected Merged, got %A" other
          } ]
