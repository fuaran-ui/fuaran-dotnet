module Fuaran.UI.OpStream.Dag.Tests.M2Tests

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
//  M2 refinements (Phase 179): SemanticStyle sub-field blending + human
//  primacy (provenance-derived pins + the three-up conflict envelope).
// ============================================================================

let private now = ts 9_000L

/// An AI-authored single-parent record (carries a PromptId → not human-pinned).
let private aiStep (parent: DagOpRecord<TestMsg> option) (op: TreeOp<TestMsg>) (prompt: string) (unix: int64) =
    let parents =
        match parent with
        | None -> []
        | Some p -> [ p.Hash ]

    DagOpRecord.create
        "s"
        parents
        op
        (Some prompt)
        (Actor.Agent("claude", "4.8", "planner"))
        (ts unix)
        OpResultEnvelope.Success

let private styleWith (f: SemanticStyle -> SemanticStyle) =
    TreeOp.UpdateStyle(leftChildId, f Defaults.style)

let private nn (value: 'T) : obj = box value |> Unchecked.nonNull

/// A Heading "left" carrying `text` — the name+type-compatible kind-swap target
/// for a pinned Markdown.Text (both kinds expose a `Text: TextSource`).
let private headingLeft (text: string) : NodeKind<TestMsg> =
    NodeKind.Heading(
        { Level = 2
          Text = TextSource.Literal text
          Variant = HeadingVariant.Standard }
    )

[<Tests>]
let tests =
    testList
        "Dag.M2"
        [ test "SemanticStyle sub-fields blend: A's Tone + B's Voice auto-merge, no conflict" {
              let sink = InMemoryDagSink.create<TestMsg> ()
              let initial = buildDashboard ()
              // base op leaves left's style at default.
              let baseOp = TreeOp.UpdateStyle(rightChildId, Defaults.style)
              let a = stepRecord "s" None baseOp 1L
              add sink a
              let toneOp = styleWith (fun s -> { s with Tone = ToneVariant.Brand })
              let voiceOp = styleWith (fun s -> { s with Voice = FontVoice.Display })
              let branchA = stepRecord "s" (Some a) toneOp 2L
              let branchB = stepRecord "s" (Some a) voiceOp 3L
              add sink branchA
              add sink branchB

              match
                  DagMerge.merge recordAuthor sink "s" initial branchA.Hash branchB.Hash now
                  |> Async.RunSynchronously
              with
              | MergeResult.Merged(_, tree) ->
                  let leftStyle =
                      match tree.Kind with
                      | NodeKind.Box(spec) ->
                          spec.Children
                          |> List.tryFind (fun c -> NodeId c.Id = leftChildId)
                          |> Option.bind _.Style
                      | _ -> None

                  match leftStyle with
                  | Some st ->
                      Expect.equal st.Tone ToneVariant.Brand "A's Tone survived"
                      Expect.equal st.Voice FontVoice.Display "B's Voice survived — sub-fields blended"
                  | None -> failtest "left node not found"
              | other -> failtestf "expected Merged (sub-field blend), got %A" other
          }

          test "human primacy: a human↔AI conflict is PinHeld with KeepHuman first + three-up values" {
              let sink = InMemoryDagSink.create<TestMsg> ()
              let initial = buildDashboard ()
              let a = stepRecord "s" None (TreeOp.UpdateStyle(rightChildId, Defaults.style)) 1L
              add sink a
              // Human edit (PromptId = None) vs AI edit (PromptId = Some) on the
              // same cell (left.tone).
              let humanEdit =
                  stepRecord "s" (Some a) (styleWith (fun s -> { s with Tone = ToneVariant.Brand })) 2L

              let aiEdit =
                  aiStep (Some a) (styleWith (fun s -> { s with Tone = ToneVariant.Critical })) "turn-5" 3L

              add sink humanEdit
              add sink aiEdit

              // human = headA, AI = headB.
              match
                  DagMerge.merge recordAuthor sink "s" initial humanEdit.Hash aiEdit.Hash now
                  |> Async.RunSynchronously
              with
              | MergeResult.NeedsManualMerge cells ->
                  let c = cells |> List.find (fun c -> c.NodeId = "left" && c.Facet = "style.tone")

                  Expect.isTrue c.PrimacyHeld "human pin held"
                  Expect.equal (List.head c.Choices) MergeChoice.KeepPrimary "KeepHuman is the default (first) choice"
                  Expect.isSome c.Primary "primary (human) value present (three-up)"
                  Expect.isSome c.Secondary "secondary (AI) value present (three-up)"
                  Expect.equal c.SecondaryTag (Some "turn-5") "AI rationale lifted from PromptId"
              | other -> failtestf "expected NeedsManualMerge (pinned), got %A" other
          }

          test "two AI sides: a conflict is not pinned (no KeepHuman default)" {
              let sink = InMemoryDagSink.create<TestMsg> ()
              let initial = buildDashboard ()
              let a = stepRecord "s" None (TreeOp.UpdateStyle(rightChildId, Defaults.style)) 1L
              add sink a

              let aiA =
                  aiStep (Some a) (styleWith (fun s -> { s with Tone = ToneVariant.Brand })) "t1" 2L

              let aiB =
                  aiStep (Some a) (styleWith (fun s -> { s with Tone = ToneVariant.Critical })) "t2" 3L

              add sink aiA
              add sink aiB

              match
                  DagMerge.merge recordAuthor sink "s" initial aiA.Hash aiB.Hash now
                  |> Async.RunSynchronously
              with
              | MergeResult.NeedsManualMerge cells ->
                  let c = cells |> List.find (fun c -> c.NodeId = "left" && c.Facet = "style.tone")

                  Expect.isFalse c.PrimacyHeld "no human pin between two AI sides"
                  Expect.isFalse (List.contains MergeChoice.KeepPrimary c.Choices) "no KeepHuman choice"
              | other -> failtestf "expected NeedsManualMerge, got %A" other
          }

          test "KindSwapOrphansPin: an AI kind-swap orphaning a human pin raises the swap class, not ConcurrentEdit" {
              let sink = InMemoryDagSink.create<TestMsg> ()
              let initial = buildDashboard ()
              let a = stepRecord "s" None (TreeOp.UpdateStyle(rightChildId, Defaults.style)) 1L
              add sink a
              // Human pins left's Markdown.Text (keeps the kind, edits a field).
              let humanPin =
                  stepRecord
                      "s"
                      (Some a)
                      (TreeOp.UpdateProp(
                          leftChildId,
                          "Text",
                          PropValue.Native(nn (TextSource.Literal "Pinned by human"))
                      ))
                      2L
              // AI swaps left Markdown → Heading (destroys the pinned cell).
              let aiSwap =
                  aiStep (Some a) (TreeOp.EditNode(leftChildId, headingLeft "AI heading")) "turn-7" 3L

              add sink humanPin
              add sink aiSwap

              match
                  DagMerge.merge recordAuthor sink "s" initial humanPin.Hash aiSwap.Hash now
                  |> Async.RunSynchronously
              with
              | MergeResult.NeedsManualMerge cells ->
                  let c = cells |> List.find (fun c -> c.NodeId = "left" && c.Facet = "kind")

                  Expect.equal c.Class MergeConflictClass.KindSwapOrphansPin "kind-swap-orphans-pin, not ConcurrentEdit"
                  Expect.isTrue c.PrimacyHeld "human pin held"

                  Expect.equal
                      (List.head c.Choices)
                      MergeChoice.ReassertPinOntoNewKind
                      "ReassertPinOntoNewKind offered first (Heading has a name+type-compatible Text field)"

                  Expect.isSome c.Primary "orphaned primary kind surfaced (three-up)"
                  Expect.equal c.SecondaryTag (Some "turn-7") "AI rationale lifted from PromptId"
              | other -> failtestf "expected NeedsManualMerge (KindSwapOrphansPin), got %A" other
          }

          test "ReassertPin migrates a pinned Markdown.Text onto the swapped-in Heading.Text (name+type-compatible)" {
              let baseLeft = Fuaran.markdown "left" "Left pane"
              let humanLeft = Fuaran.markdown "left" "Pinned by human"

              let aiLeft: Node<TestMsg> =
                  { Fuaran.markdown "left" "ignored" with
                      Kind = headingLeft "AI heading" }

              match ReassertPin.tryReassert baseLeft humanLeft aiLeft with
              | Some(NodeKind.Heading(spec)) ->
                  match spec.Text with
                  | TextSource.Literal s ->
                      Expect.equal s "Pinned by human" "human's pinned Text re-stamped onto the new Heading"
                  | other -> failtestf "expected a Literal Text, got %A" other

                  Expect.equal spec.Level 2 "the AI's new-kind shape is otherwise preserved"
              | other -> failtestf "expected a migrated Heading kind, got %A" other
          }

          test "ReassertPin returns None when the new kind has no name+type-compatible field (KeepOldKind path)" {
              let baseLeft = Fuaran.markdown "left" "Left pane"
              let humanLeft = Fuaran.markdown "left" "Pinned by human"
              // Metric exposes no `Text` field — the pin cannot migrate.
              let aiLeft = Fuaran.metric "left" Defaults.metric

              Expect.isNone
                  (ReassertPin.tryReassert baseLeft humanLeft aiLeft)
                  "no compatible field ⇒ None (caller offers KeepOldKind)"
          }

          test
              "per-cell pin: a human pin survives a LATER AI edit to a different cell on the same branch (branch tip is AI)" {
              let sink = InMemoryDagSink.create<TestMsg> ()
              let initial = buildDashboard ()
              let a = stepRecord "s" None (TreeOp.UpdateStyle(rightChildId, Defaults.style)) 1L
              add sink a

              // Branch A: a HUMAN pins left.tone = Brand, then an AI edits a
              // DIFFERENT cell (right.tone) on top — so branch A's TIP is AI,
              // though left.tone's last writer is the human.
              let humanTone =
                  stepRecord "s" (Some a) (styleWith (fun s -> { s with Tone = ToneVariant.Brand })) 2L

              let aiOtherCell =
                  aiStep
                      (Some humanTone)
                      (TreeOp.UpdateStyle(
                          rightChildId,
                          { Defaults.style with
                              Tone = ToneVariant.Critical }
                      ))
                      "turn-9"
                      3L

              // Branch B: an AI edits left.tone = Success (conflicts with the pin).
              let aiLeftTone =
                  aiStep (Some a) (styleWith (fun s -> { s with Tone = ToneVariant.Critical })) "turn-3" 4L

              add sink humanTone
              add sink aiOtherCell
              add sink aiLeftTone

              match
                  DagMerge.merge recordAuthor sink "s" initial aiOtherCell.Hash aiLeftTone.Hash now
                  |> Async.RunSynchronously
              with
              | MergeResult.NeedsManualMerge cells ->
                  let c = cells |> List.find (fun c -> c.NodeId = "left" && c.Facet = "style.tone")

                  // Per-branch-tip authorship would call branch A "AI" (its tip is
                  // the right-edit) and so leave left.tone UNPINNED. The per-cell
                  // backward walk attributes left.tone to the human writer.
                  Expect.isTrue c.PrimacyHeld "left.tone is pinned to the human despite the AI tip"
                  Expect.equal (List.head c.Choices) MergeChoice.KeepPrimary "KeepHuman is the default choice"
                  Expect.equal c.SecondaryTag (Some "turn-3") "rationale lifted from the conflicting AI op (branch B)"
              | other -> failtestf "expected NeedsManualMerge (per-cell pin), got %A" other
          } ]
