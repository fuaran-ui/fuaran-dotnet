module Fuaran.UI.OpStream.Tests.TreeDiffTests

open Expecto
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Ops
open Fuaran.UI.Ops.Types
open Fuaran.UI.OpStream.Abstractions
open Fuaran.UI.OpStream.Replay
open Fuaran.UI.OpStream.Tests.TestSupport

// ============================================================================
//  TreeDiff correctness — per-op-kind classification + per-step replay-diff +
//  integrity preflight + apply-failure partial-progress.
//
//  Pairs with ReplayTests.fs (which exercises Replay.applyTo in
//  isolation); these tests verify the diff produced by composing apply +
//  diff matches what each op kind structurally implies.
// ============================================================================

// ─── Helpers ──────────────────────────────────────────────────────────────

let private rawId (NodeId raw) = raw

/// F# 10 nullness escape — `box value` lights up as `objnull` and
/// `PropValue.Native` carries a non-null `obj`. Same shape as the helper in
/// Fuaran.UI.Ops.Tests/OpsApplyTests.fs.
let private nn (value: 'T) : obj = box value |> Unchecked.nonNull

let private countByKind (diff: TreeDiff) =
    let mutable added = 0
    let mutable removed = 0
    let mutable moved = 0
    let mutable kindChanged = 0
    let mutable propChanged = 0
    let mutable textChanged = 0

    for change in diff.Changes do
        match change.Change with
        | NodeChangeKind.Added(_, _) -> added <- added + 1
        | NodeChangeKind.Removed _ -> removed <- removed + 1
        | NodeChangeKind.Moved(_, _, _, _) -> moved <- moved + 1
        | NodeChangeKind.KindChanged(_, _) -> kindChanged <- kindChanged + 1
        | NodeChangeKind.PropChanged(_, _) -> propChanged <- propChanged + 1
        | NodeChangeKind.TextChanged(_, _) -> textChanged <- textChanged + 1

    added, removed, moved, kindChanged, propChanged, textChanged

let private hasChangeFor (rawIdNeedle: string) (kindPred: NodeChangeKind -> bool) (diff: TreeDiff) : bool =
    diff.Changes
    |> List.exists (fun c -> rawId c.NodeId = rawIdNeedle && kindPred c.Change)

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
                  let result = TreeDiff.diff before after
                  let _, removed, _, _, _, _ = countByKind result
                  Expect.equal removed 1 "Exactly one removal"

                  let isRightRemoved (c: NodeChangeKind) =
                      match c with
                      | NodeChangeKind.Removed _ -> true
                      | _ -> false

                  Expect.isTrue (hasChangeFor "right" isRightRemoved result) "Right node is the removed one"
              | Error e -> failtestf "Apply failed: %A" e
          }

          test "InsertChild op produces a single Added entry with the right parent + position" {
              let before = buildDashboard ()

              let newChild: Node<TestMsg> = Fuaran.markdown "middle" "Middle pane"

              match Apply.apply (TreeOp.InsertChild(NodeId "dash", 1, newChild)) before with
              | Ok after ->
                  let result = TreeDiff.diff before after
                  let added, _, _, _, _, _ = countByKind result
                  Expect.equal added 1 "Exactly one addition"

                  let isMiddleAdded (c: NodeChangeKind) =
                      match c with
                      | NodeChangeKind.Added(Some(NodeId "dash"), 1) -> true
                      | _ -> false

                  Expect.isTrue (hasChangeFor "middle" isMiddleAdded result) "Middle inserted under dash at position 1"
              | Error e -> failtestf "Apply failed: %A" e
          }

          test "ReorderChildren op produces Moved entries for re-positioned children only" {
              let before = buildDashboard ()
              // Swap left ↔ right.
              let op = TreeOp.ReorderChildren(NodeId "dash", [ NodeId "right"; NodeId "left" ])

              match Apply.apply op before with
              | Ok after ->
                  let result = TreeDiff.diff before after
                  let _, _, moved, _, _, _ = countByKind result
                  Expect.equal moved 2 "Both children moved"

                  let isMoveTo (toPos: int) (c: NodeChangeKind) =
                      match c with
                      | NodeChangeKind.Moved(_, _, Some(NodeId "dash"), p) when p = toPos -> true
                      | _ -> false

                  Expect.isTrue (hasChangeFor "right" (isMoveTo 0) result) "Right moved to position 0"
                  Expect.isTrue (hasChangeFor "left" (isMoveTo 1) result) "Left moved to position 1"
              | Error e -> failtestf "Apply failed: %A" e
          }

          test "MoveNode op produces a Moved entry with from/to parent + position" {
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

              // Move 'leaf' from 'inner' (position 0) to 'outer' (position 1).
              let op = TreeOp.MoveNode(NodeId "leaf", NodeId "outer", 1)

              match Apply.apply op outer with
              | Ok after ->
                  let result = TreeDiff.diff outer after
                  let _, _, moved, _, _, _ = countByKind result
                  Expect.isGreaterThanOrEqual moved 1 "At least one move"

                  let isLeafMoveAcrossParents (c: NodeChangeKind) =
                      match c with
                      | NodeChangeKind.Moved(Some(NodeId "inner"), 0, Some(NodeId "outer"), 1) -> true
                      | _ -> false

                  Expect.isTrue
                      (hasChangeFor "leaf" isLeafMoveAcrossParents result)
                      "Leaf moved from inner/0 to outer/1"
              | Error e -> failtestf "Apply failed: %A" e
          }

          test "UpdateProp on Markdown.Text produces PropChanged + TextChanged" {
              let before = buildDashboard ()
              // The 'left' child is a Markdown — rewrite its text via UpdateProp.
              let newTextSource = TextSource.Literal "Left pane (updated)"

              let op =
                  TreeOp.UpdateProp(NodeId "left", "Text", PropValue.Native(nn newTextSource))

              match Apply.apply op before with
              | Ok after ->
                  let result = TreeDiff.diff before after
                  let _, _, _, _, propChanged, textChanged = countByKind result
                  Expect.isGreaterThanOrEqual propChanged 1 "At least one prop change"
                  Expect.equal textChanged 1 "One text change (Markdown leaf)"

                  let isLeftTextChange (c: NodeChangeKind) =
                      match c with
                      | NodeChangeKind.TextChanged("Left pane", "Left pane (updated)") -> true
                      | _ -> false

                  Expect.isTrue (hasChangeFor "left" isLeftTextChange result) "Left's text delta surfaced"
              | Error e -> failtestf "Apply failed: %A" e
          }

          test "EditNode op (Markdown → Badge) produces KindChanged" {
              let before = buildDashboard ()

              let newKind: NodeKind<TestMsg> =
                  NodeKind.Display(
                      DisplayKind.Badge
                          { Label = TextSource.Literal "STATUS"
                            Variant = BadgeVariant.Brand }
                  )

              let op = TreeOp.EditNode(NodeId "left", newKind)

              match Apply.apply op before with
              | Ok after ->
                  let result = TreeDiff.diff before after
                  let _, _, _, kindChanged, _, _ = countByKind result
                  Expect.equal kindChanged 1 "One kind change"

                  let isMarkdownToBadge (c: NodeChangeKind) =
                      match c with
                      | NodeChangeKind.KindChanged("Markdown", "Badge") -> true
                      | _ -> false

                  Expect.isTrue (hasChangeFor "left" isMarkdownToBadge result) "Left's kind flipped Markdown→Badge"
              | Error e -> failtestf "Apply failed: %A" e
          }

          test "Round-trip — apply a chain of ops then diff(start, end) ⊇ cumulative effect" {
              // Op sequence: remove 'right', insert 'middle' between 'left' and the gap.
              let start = buildDashboard ()
              let op1 = TreeOp.RemoveNode(NodeId "right"): TreeOp<TestMsg>

              let op2 =
                  TreeOp.InsertChild(NodeId "dash", 1, Fuaran.markdown "middle" "Middle pane")

              match Apply.apply op1 start with
              | Ok t1 ->
                  match Apply.apply op2 t1 with
                  | Ok t2 ->
                      let cumulative = TreeDiff.diff start t2

                      Expect.isTrue
                          (hasChangeFor
                              "right"
                              (function
                              | NodeChangeKind.Removed _ -> true
                              | _ -> false)
                              cumulative)
                          "Cumulative diff records 'right' removed"

                      Expect.isTrue
                          (hasChangeFor
                              "middle"
                              (function
                              | NodeChangeKind.Added(_, _) -> true
                              | _ -> false)
                              cumulative)
                          "Cumulative diff records 'middle' added"

                      let added, removed, _, _, _, _ = countByKind cumulative
                      Expect.equal added 1 "One net addition"
                      Expect.equal removed 1 "One net removal"
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
                  TreeOp.InsertChild(NodeId "dash", 1, Fuaran.markdown "middle" "Middle"): TreeOp<TestMsg>

              let r1 = buildRecord "stream" 1 op1 None (timestamp 100L)
              let r2 = buildRecord "stream" 2 op2 (Some r1) (timestamp 200L)

              match TreeDiff.stepDiffs start [ r1; r2 ] with
              | Ok steps ->
                  Expect.equal (List.length steps) 2 "One StepDiff per op"
                  // First step: 'right' removed.
                  let step1 = steps[0]
                  Expect.equal step1.Record.Sequence 1 "First step is record 1"

                  Expect.isTrue
                      (hasChangeFor
                          "right"
                          (function
                          | NodeChangeKind.Removed _ -> true
                          | _ -> false)
                          step1.Diff)
                      "Step 1 diff records 'right' removed"
                  // Second step: 'middle' added.
                  let step2 = steps[1]
                  Expect.equal step2.Record.Sequence 2 "Second step is record 2"

                  Expect.isTrue
                      (hasChangeFor
                          "middle"
                          (function
                          | NodeChangeKind.Added(_, _) -> true
                          | _ -> false)
                          step2.Diff)
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
                  TreeOp.InsertChild(NodeId "dash", 1, Fuaran.markdown "middle" "Middle"): TreeOp<TestMsg>

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
