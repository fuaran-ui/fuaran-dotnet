module Fuaran.UI.OpStream.Dag.Tests.ValidatorGateTests

open Expecto
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Ops.Types
open Fuaran.UI.OpStream.Dag.InMemory
open Fuaran.UI.OpStream.Dag.Merge
open Fuaran.UI.OpStream.Dag.Tests.TestSupport

// ============================================================================
//  Validator-gated merge (Phase 184): a structurally-clean, NodeId-disjoint
//  merge that nonetheless introduces a DOMAIN-validity defect is a semantic
//  conflict, surfaced through the Phase 179 `MergeConflict` envelope
//  (`CombinedCycle`). A defect already present in a parent is carried through,
//  never flagged. Gating is policy-controlled.
// ============================================================================

let private now = ts 9_000L

/// A sample DOMAIN validator — the plug-point each domain supplies its own of.
/// Flags a dashboard whose direct children include MORE THAN ONE `Brand`-toned
/// pane: a "duplicate brand emphasis" sibling invariant (at most one Brand pane
/// per dashboard). Each offending child is a defect on its `style.tone` cell.
/// Node-local in appearance, but only manifests on the COMBINATION — so a
/// disjoint merge of two individually-legal branches can INTRODUCE it.
let private brandSiblingValidator: MergeValidator<TestMsg> =
    fun tree ->
        match tree.Kind with
        | NodeKind.Box(spec) ->
            let brandKids =
                spec.Children
                |> List.filter (fun c -> (c.Style |> Option.defaultValue Defaults.style).Tone = ToneVariant.Brand)

            if List.length brandKids > 1 then
                brandKids
                |> List.map (fun c ->
                    { Code = "TESTBRAND001"
                      NodeId = c.Id
                      Facet = "style.tone"
                      Message =
                        sprintf
                            "Pane '%s' shares Brand tone with a sibling — at most one Brand pane per dashboard."
                            c.Id })
            else
                []
        | _ -> []

/// `UpdateStyle` op setting `id`'s tone.
let private toneOp (id: NodeId) (tone: ToneVariant) : TreeOp<TestMsg> =
    TreeOp.UpdateStyle(id, { Defaults.style with Tone = tone })

/// `UpdateStyle` op setting `id`'s weight (a tone-neutral facet) — used to build
/// a disjoint structurally-clean merge that does NOT touch the validated cell.
let private weightOp (id: NodeId) (w: StyleWeight) : TreeOp<TestMsg> =
    TreeOp.UpdateStyle(id, { Defaults.style with Weight = w })

[<Tests>]
let tests =
    testList
        "Dag.ValidatorGate"
        [ test "merge-introduced defect (gating ON): a disjoint merge that creates a sibling-invalid tree is REFUSED" {
              let sink = InMemoryDagSink.create<TestMsg> ()
              let initial = buildDashboard ()
              // base: neither pane is Brand (no defect).
              let a = stepRecord "s" None (toneOp rightChildId ToneVariant.Default) 1L
              add sink a
              // branchA makes LEFT Brand; branchB makes RIGHT Brand. Each branch
              // alone has a single Brand pane (legal); the disjoint merge has two.
              let branchA = stepRecord "s" (Some a) (toneOp leftChildId ToneVariant.Brand) 2L
              let branchB = stepRecord "s" (Some a) (toneOp rightChildId ToneVariant.Brand) 3L
              add sink branchA
              add sink branchB

              let outcome =
                  DagMerge.mergeGated
                      recordAuthor
                      sink
                      "s"
                      initial
                      branchA.Hash
                      branchB.Hash
                      now
                      (MergePolicy.gated brandSiblingValidator)
                  |> Async.RunSynchronously

              match outcome.Result with
              | MergeResult.NeedsManualMerge cells ->
                  Expect.equal cells.Length 2 "both offending panes named"

                  Expect.isTrue
                      (cells |> List.forall (fun c -> c.Class = MergeConflictClass.CombinedCycle))
                      "introduced defect lifts into the CombinedCycle conflict class"

                  let ids = cells |> List.map _.NodeId |> List.sort
                  Expect.equal ids [ "left"; "right" ] "the offending nodes are named (left + right)"

                  Expect.isTrue
                      (cells |> List.forall (fun c -> c.Base = "TESTBRAND001"))
                      "the validator's defect code rides in the conflict envelope"

                  Expect.isTrue
                      (cells |> List.forall (fun c -> c.Choices = [ MergeChoice.KeepBase ]))
                      "recovery is enumerated (KeepBase abandons the merge for the cell)"

                  Expect.equal outcome.Diagnostics.Length 2 "the introduced defects are also reported as diagnostics"
              | other -> failtestf "expected NeedsManualMerge (validator-gated refusal), got %A" other
          }

          test "clean merge (gating ON): a disjoint merge introducing NO defect proceeds normally" {
              let sink = InMemoryDagSink.create<TestMsg> ()
              let initial = buildDashboard ()
              let a = stepRecord "s" None (toneOp rightChildId ToneVariant.Default) 1L
              add sink a
              // disjoint, non-Brand edits — the merged tree has zero Brand panes.
              let branchA = stepRecord "s" (Some a) (toneOp leftChildId ToneVariant.Success) 2L
              let branchB = stepRecord "s" (Some a) (toneOp rightChildId ToneVariant.Critical) 3L
              add sink branchA
              add sink branchB

              let outcome =
                  DagMerge.mergeGated
                      recordAuthor
                      sink
                      "s"
                      initial
                      branchA.Hash
                      branchB.Hash
                      now
                      (MergePolicy.gated brandSiblingValidator)
                  |> Async.RunSynchronously

              match outcome.Result with
              | MergeResult.Merged _ -> Expect.isEmpty outcome.Diagnostics "no introduced defect ⇒ no diagnostics"
              | other -> failtestf "expected Merged (clean), got %A" other
          }

          test "parent-preexisting defect (gating ON): a defect already in a parent is carried through, NOT flagged" {
              let sink = InMemoryDagSink.create<TestMsg> ()
              let initial = buildDashboard ()
              // base ALREADY violates the invariant: both panes Brand.
              let leftBrand = stepRecord "s" None (toneOp leftChildId ToneVariant.Brand) 1L
              add sink leftBrand

              let bothBrand =
                  stepRecord "s" (Some leftBrand) (toneOp rightChildId ToneVariant.Brand) 2L

              add sink bothBrand
              // disjoint, tone-NEUTRAL edits on each branch — the both-Brand defect
              // persists in both parents and in the merge, but is not merge-caused.
              let branchA =
                  stepRecord "s" (Some bothBrand) (weightOp leftChildId StyleWeight.Spacious) 3L

              let branchB =
                  stepRecord "s" (Some bothBrand) (weightOp rightChildId StyleWeight.Compact) 4L

              add sink branchA
              add sink branchB

              let outcome =
                  DagMerge.mergeGated
                      recordAuthor
                      sink
                      "s"
                      initial
                      branchA.Hash
                      branchB.Hash
                      now
                      (MergePolicy.gated brandSiblingValidator)
                  |> Async.RunSynchronously

              match outcome.Result with
              | MergeResult.Merged _ ->
                  Expect.isEmpty
                      outcome.Diagnostics
                      "a parent-preexisting defect is carried through, never reported as merge-introduced"
              | other -> failtestf "expected Merged (carried-through defect), got %A" other
          }

          test
              "gating OFF (diagnostic policy): the clean structural merge proceeds; introduced defect is a diagnostic only" {
              let sink = InMemoryDagSink.create<TestMsg> ()
              let initial = buildDashboard ()
              let a = stepRecord "s" None (toneOp rightChildId ToneVariant.Default) 1L
              add sink a
              let branchA = stepRecord "s" (Some a) (toneOp leftChildId ToneVariant.Brand) 2L
              let branchB = stepRecord "s" (Some a) (toneOp rightChildId ToneVariant.Brand) 3L
              add sink branchA
              add sink branchB

              let outcome =
                  DagMerge.mergeGated
                      recordAuthor
                      sink
                      "s"
                      initial
                      branchA.Hash
                      branchB.Hash
                      now
                      (MergePolicy.diagnostic brandSiblingValidator)
                  |> Async.RunSynchronously

              match outcome.Result with
              | MergeResult.Merged _ ->
                  Expect.equal
                      outcome.Diagnostics.Length
                      2
                      "gating off ⇒ the merge proceeds, but the introduced defect is surfaced as a diagnostic"
              | other -> failtestf "expected Merged + diagnostic (gating off), got %A" other
          }

          test "lenient policy ≡ ungated merge: mergeGated with MergePolicy.lenient matches DagMerge.merge" {
              let sink = InMemoryDagSink.create<TestMsg> ()
              let initial = buildDashboard ()
              let a = stepRecord "s" None (toneOp rightChildId ToneVariant.Default) 1L
              add sink a
              let branchA = stepRecord "s" (Some a) (toneOp leftChildId ToneVariant.Brand) 2L
              let branchB = stepRecord "s" (Some a) (toneOp rightChildId ToneVariant.Brand) 3L
              add sink branchA
              add sink branchB

              let gated =
                  DagMerge.mergeGated recordAuthor sink "s" initial branchA.Hash branchB.Hash now MergePolicy.lenient
                  |> Async.RunSynchronously

              let ungated =
                  DagMerge.merge recordAuthor sink "s" initial branchA.Hash branchB.Hash now
                  |> Async.RunSynchronously

              // No validator ⇒ the introduced sibling-invalid tree still auto-merges.
              match gated.Result, ungated with
              | MergeResult.Merged(_, gt), MergeResult.Merged(_, ut) ->
                  Expect.equal (canonical gt) (canonical ut) "lenient gated merge tree == ungated merge tree"
                  Expect.isEmpty gated.Diagnostics "lenient policy emits no diagnostics"
              | g, u -> failtestf "expected both Merged, got %A / %A" g u
          }

          test "verdict determinism: encodeVerdict is byte-stable + walker-order-independent" {
              let d1 =
                  { Code = "TESTBRAND001"
                    NodeId = "right"
                    Facet = "style.tone"
                    Message = "b" }

              let d2 =
                  { Code = "TESTBRAND001"
                    NodeId = "left"
                    Facet = "style.tone"
                    Message = "a" }

              // Same defect set in two different emission orders ⇒ identical bytes.
              Expect.equal
                  (ValidatorGate.encodeVerdict [ d1; d2 ])
                  (ValidatorGate.encodeVerdict [ d2; d1 ])
                  "verdict is independent of walker emission order"

              Expect.stringContains
                  (ValidatorGate.encodeVerdict [ d1; d2 ])
                  "\"nodeId\":\"left\""
                  "verdict names the offending nodes"
          } ]
