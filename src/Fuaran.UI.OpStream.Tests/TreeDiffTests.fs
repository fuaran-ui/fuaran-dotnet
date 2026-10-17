module Fuaran.UI.OpStream.Tests.TreeDiffTests

open Expecto
open Fuaran.Core
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Ops
open Fuaran.UI.Ops.Types
open Fuaran.UI.OpStream.Abstractions
open Fuaran.UI.OpStream.Replay
open Fuaran.UI.OpStream.TestSupport

// ============================================================================
//  TreeDiff correctness — per-op-kind classification + per-step replay-diff +
//  integrity preflight + apply-failure partial-progress.
//
//  The classification is Fuaran.Core's `Diff.changes` (Phase 2304), so these
//  tests read Core's vocabulary: a same-parent reposition is `Reordered` on
//  the parent, a content change is `Changed`.
// ============================================================================

// ─── Helpers ──────────────────────────────────────────────────────────────

/// F# 10 nullness escape — `box value` lights up as `objnull` and
/// `PropValue.Native` carries a non-null `obj`. Same shape as the helper in
/// Fuaran.UI.Ops.Tests/OpsApplyTests.fs.
let private nn (value: 'T) : obj = box value |> Unchecked.nonNull

/// The diff's entries as `(id, kind)` pairs, in the diff's own order.
let private entries (diff: TreeDiff) : (string * Diff.ChangeKind<NodeId>) list =
    diff.Changes |> List.map (fun c -> (let (NodeId raw) = c.Id in raw), c.Kind)

let private hasChangeFor (rawIdNeedle: string) (kind: Diff.ChangeKind<NodeId>) (diff: TreeDiff) : bool =
    entries diff |> List.contains (rawIdNeedle, kind)

// ─── Per-op-kind diff classification ─────────────────────────────────────

[<Tests>]
let tests =
    testList
        "Fuaran.UI.OpStream — TreeDiff"
        [ test "diff of a tree against itself is empty" {
              let tree = buildDashboard ()
              let result = TreeDiff.diff tree tree
              Expect.isEmpty result.Changes "Identity diff has no changes"
          }

          test "RemoveNode op produces a single Removed entry" {
              let before = buildDashboard ()

              match Apply.apply (TreeOp.RemoveNode(NodeId "right")) before with
              | Ok after ->
                  Expect.equal
                      (entries (TreeDiff.diff before after))
                      [ "right", Diff.ChangeKind.Removed ]
                      "exactly the removed node, nothing about its parent (a child leaving alone is not a reorder)"
              | Error e -> failtestf "Apply failed: %A" e
          }

          test "InsertChild op produces a single Added entry" {
              let before = buildDashboard ()

              let newChild: Node<TestMsg> = Fuaran.markdown "middle" "Middle pane"

              match Apply.apply (TreeOp.InsertChild(NodeId "dash", newChild)) before with
              | Ok after ->
                  Expect.equal
                      (entries (TreeDiff.diff before after))
                      [ "middle", Diff.ChangeKind.Added ]
                      "exactly the inserted node"
              | Error e -> failtestf "Apply failed: %A" e
          }

          test "ReorderChildren op produces one Reordered entry, on the parent" {
              let before = buildDashboard ()
              // Swap left ↔ right.
              let op = TreeOp.ReorderChildren(NodeId "dash", [ NodeId "right"; NodeId "left" ])

              match Apply.apply op before with
              | Ok after ->
                  Expect.equal
                      (entries (TreeDiff.diff before after))
                      [ "dash", Diff.ChangeKind.Reordered ]
                      "a same-parent reposition is the PARENT's reorder, not a move of each child"
              | Error e -> failtestf "Apply failed: %A" e
          }

          test "MoveNode op produces a Moved entry naming both parents" {
              // Build a tree with two layers so MoveNode has somewhere to go.
              let inner: Node<TestMsg> =
                  Fuaran.dashboard
                      "inner"
                      { Defaults.dashboard<TestMsg> with
                          Children = [ Fuaran.markdown "leaf" "Leaf" ] }

              let outer: Node<TestMsg> =
                  Fuaran.dashboard
                      "outer"
                      { Defaults.dashboard<TestMsg> with
                          Children = [ inner; Fuaran.markdown "side" "Side" ] }

              let op = TreeOp.MoveNode(NodeId "leaf", NodeId "outer")

              match Apply.apply op outer with
              | Ok after ->
                  Expect.equal
                      (entries (TreeDiff.diff outer after))
                      [ "leaf", Diff.ChangeKind.Moved(NodeId "inner", NodeId "outer") ]
                      "the leaf moved from inner to outer; an arriving child alone reorders nothing"
              | Error e -> failtestf "Apply failed: %A" e
          }

          test "UpdateProp on Markdown.Text produces Changed" {
              let before = buildDashboard ()
              let newTextSource = TextSource.Literal "Left pane (updated)"

              let op =
                  TreeOp.UpdateProp(NodeId "left", "Text", PropValue.Native(nn newTextSource))

              match Apply.apply op before with
              | Ok after ->
                  Expect.equal
                      (entries (TreeDiff.diff before after))
                      [ "left", Diff.ChangeKind.Changed ]
                      "a content change of a survivor is Changed (the old TextChanged / PropChanged pair is one reading)"
              | Error e -> failtestf "Apply failed: %A" e
          }

          test "EditNode op (Markdown → Badge) produces KindChanged instead of Changed" {
              let before = buildDashboard ()

              let newKind: NodeKind<TestMsg> =
                  NodeKind.Badge(
                      { Label = TextSource.Literal "STATUS"
                        Variant = BadgeVariant.Brand }
                  )

              match Apply.apply (TreeOp.EditNode(NodeId "left", newKind)) before with
              | Ok after ->
                  Expect.equal
                      (entries (TreeDiff.diff before after))
                      [ "left", Diff.ChangeKind.KindChanged("Markdown", "Badge") ]
                      "Left's kind flipped Markdown→Badge"
              | Error e -> failtestf "Apply failed: %A" e
          }

          test "a moved AND changed survivor carries both entries, in Core's order" {
              let inner: Node<TestMsg> =
                  Fuaran.dashboard
                      "inner"
                      { Defaults.dashboard<TestMsg> with
                          Children = [ Fuaran.markdown "leaf" "Leaf" ] }

              let before: Node<TestMsg> =
                  Fuaran.dashboard
                      "outer"
                      { Defaults.dashboard<TestMsg> with
                          Children = [ inner ] }

              let after: Node<TestMsg> =
                  Fuaran.dashboard
                      "outer"
                      { Defaults.dashboard<TestMsg> with
                          Children =
                              [ Fuaran.dashboard "inner" Defaults.dashboard<TestMsg>
                                Fuaran.markdown "leaf" "Leaf, edited" ] }

              Expect.equal
                  (entries (TreeDiff.diff before after))
                  [ "leaf", Diff.ChangeKind.Moved(NodeId "inner", NodeId "outer")
                    "leaf", Diff.ChangeKind.Changed ]
                  "Moved then Changed for one id"
          }

          test "the classification is the projection of the script TreeOpDiff emits" {
              // Core's D112 over the tier's witnesses: the Added ids are the
              // inserted ids, the Moved ids the MoveNode targets, the Changed /
              // KindChanged ids the nodes the script rewrites.
              let before = buildDashboard ()

              let after: Node<TestMsg> =
                  Fuaran.dashboard
                      "dash"
                      { Defaults.dashboard<TestMsg> with
                          Children =
                              [ Fuaran.dashboard
                                    "group"
                                    { Defaults.dashboard<TestMsg> with
                                        Children = [ Fuaran.markdown "right" "Right pane" ] }
                                Fuaran.markdown "left" "Left pane, edited" ] }

              let changes = entries (TreeDiff.diff before after)
              let script = TreeOpDiff.diff before after

              let idsOf (pick: Diff.ChangeKind<NodeId> -> bool) =
                  changes |> List.filter (snd >> pick) |> List.map fst |> Set.ofList

              let inserted =
                  script
                  |> List.choose (function
                      | TreeOp.InsertChild(_, n) -> Some n.Id
                      | _ -> None)
                  |> Set.ofList

              let moved =
                  script
                  |> List.choose (function
                      | TreeOp.MoveNode(NodeId t, _) -> Some t
                      | _ -> None)
                  |> Set.ofList

              let rewritten =
                  script
                  |> List.choose (function
                      | TreeOp.UpdateProp(NodeId t, _, _)
                      | TreeOp.EditNode(NodeId t, _) -> Some t
                      | _ -> None)
                  |> Set.ofList

              Expect.equal
                  (idsOf (function
                      | Diff.ChangeKind.Added -> true
                      | _ -> false))
                  inserted
                  "Added = the inserted ids"

              Expect.equal
                  (idsOf (function
                      | Diff.ChangeKind.Moved _ -> true
                      | _ -> false))
                  moved
                  "Moved = the MoveNode targets"

              Expect.equal
                  (idsOf (function
                      | Diff.ChangeKind.Changed
                      | Diff.ChangeKind.KindChanged _ -> true
                      | _ -> false))
                  rewritten
                  "Changed = the rewritten ids"
          }

          test "two roots with different ids read as the wholesale replace TreeOpDiff emits" {
              let before: Node<TestMsg> = Fuaran.markdown "a" "A"
              let after: Node<TestMsg> = Fuaran.markdown "b" "B"

              Expect.equal
                  (entries (TreeDiff.diff before after))
                  [ "a", Diff.ChangeKind.Removed; "b", Diff.ChangeKind.Added ]
                  "every old id removed, every new id added"

              Expect.equal
                  (TreeOpDiff.diff before after |> List.map CanonicalJson.encodeOp)
                  [ CanonicalJson.encodeOp (TreeOp.ReplaceRoot after) ]
                  "the script is ReplaceRoot"
          }

          test "Round-trip — apply a chain of ops then diff(start, end) ⊇ cumulative effect" {
              // Op sequence: remove 'right', insert 'middle'.
              let start = buildDashboard ()
              let op1 = TreeOp.RemoveNode(NodeId "right"): TreeOp<TestMsg>

              let op2 = TreeOp.InsertChild(NodeId "dash", Fuaran.markdown "middle" "Middle pane")

              match Apply.apply op1 start with
              | Ok t1 ->
                  match Apply.apply op2 t1 with
                  | Ok t2 ->
                      Expect.equal
                          (entries (TreeDiff.diff start t2))
                          [ "middle", Diff.ChangeKind.Added; "right", Diff.ChangeKind.Removed ]
                          "one net addition and one net removal, in ascending id order"
                  | Error e -> failtestf "op2 apply failed: %A" e
              | Error e -> failtestf "op1 apply failed: %A" e
          }

          // ─── Per-step replay-diff ────────────────────────────────────

          test "stepDiffs against an empty record set returns Ok []" {
              let tree = buildDashboard ()

              match TreeDiff.stepDiffs tree [] with
              | Ok steps -> Expect.isEmpty steps "Empty input → empty output"
              | Error e -> failtestf "Expected Ok, got %A" e
          }

          test "stepDiffs produces one StepDiff per successful op" {
              let start = buildDashboard ()
              let op1 = TreeOp.RemoveNode(NodeId "right"): TreeOp<TestMsg>

              let op2 =
                  TreeOp.InsertChild(NodeId "dash", Fuaran.markdown "middle" "Middle"): TreeOp<TestMsg>

              let r1 = buildRecord "stream" 1 op1 None (timestamp 100L)
              let r2 = buildRecord "stream" 2 op2 (Some r1) (timestamp 200L)

              match TreeDiff.stepDiffs start [ r1; r2 ] with
              | Ok steps ->
                  Expect.equal (List.length steps) 2 "One StepDiff per op"
                  // First step: 'right' removed.
                  let step1 = steps[0]
                  Expect.equal step1.Record.Sequence 1 "First step is record 1"

                  Expect.isTrue
                      (hasChangeFor "right" Diff.ChangeKind.Removed step1.Diff)
                      "Step 1 diff records 'right' removed"
                  // Second step: 'middle' added.
                  let step2 = steps[1]
                  Expect.equal step2.Record.Sequence 2 "Second step is record 2"

                  Expect.isTrue
                      (hasChangeFor "middle" Diff.ChangeKind.Added step2.Diff)
                      "Step 2 diff records 'middle' added"
              | Error e -> failtestf "Expected Ok, got %A" e
          }

          // ─── Integrity preflight ─────────────────────────────────────

          test "stepDiffs surfaces IntegrityFailed when previousHash is forged" {
              let start = buildDashboard ()
              let op = TreeOp.RemoveNode(NodeId "right"): TreeOp<TestMsg>
              let genuine = buildRecord "stream" 1 op None (timestamp 100L)
              // Forge: rewrite PreviousHash so the chain verifier fails.
              let forged =
                  { genuine with
                      PreviousHash = String.replicate 64 "1" }

              match TreeDiff.stepDiffs start [ forged ] with
              | Error(StepDiffsError.IntegrityFailed verr) ->
                  match verr with
                  | VerificationError.PreviousHashMismatch(seq, _, _) ->
                      Expect.equal seq 1 "Integrity error names the forged record"
                  | other -> failtestf "Expected PreviousHashMismatch, got %A" other
              | other -> failtestf "Expected IntegrityFailed, got %A" other
          }

          test "stepDiffs surfaces IntegrityFailed on out-of-order sequence" {
              let start = buildDashboard ()
              let op1 = TreeOp.RemoveNode(NodeId "right"): TreeOp<TestMsg>

              let op2 =
                  TreeOp.InsertChild(NodeId "dash", Fuaran.markdown "middle" "Middle"): TreeOp<TestMsg>

              let r1 = buildRecord "stream" 1 op1 None (timestamp 100L)
              let r2 = buildRecord "stream" 2 op2 (Some r1) (timestamp 200L)

              match TreeDiff.stepDiffs start [ r2; r1 ] with
              | Error(StepDiffsError.IntegrityFailed verr) ->
                  match verr with
                  | VerificationError.OutOfOrder(_, _) -> ()
                  | other -> failtestf "Expected OutOfOrder, got %A" other
              | other -> failtestf "Expected IntegrityFailed, got %A" other
          }

          // ─── Apply-failure partial-progress ──────────────────────────

          test "stepDiffs surfaces ReplayFailed with the completed steps preserved" {
              let start = buildDashboard ()
              let goodOp = TreeOp.RemoveNode(NodeId "right"): TreeOp<TestMsg>
              // Second op targets a node that no longer exists — apply fails.
              let badOp = TreeOp.RemoveNode(NodeId "right"): TreeOp<TestMsg>
              let r1 = buildRecord "stream" 1 goodOp None (timestamp 100L)
              let r2 = buildRecord "stream" 2 badOp (Some r1) (timestamp 200L)

              match TreeDiff.stepDiffs start [ r1; r2 ] with
              | Error(StepDiffsError.ReplayFailed(completed, failedAt, _)) ->
                  Expect.equal (List.length completed) 1 "One step completed before the failure"
                  Expect.equal failedAt 2 "Failure points at sequence 2"
              | other -> failtestf "Expected ReplayFailed, got %A" other
          } ]
