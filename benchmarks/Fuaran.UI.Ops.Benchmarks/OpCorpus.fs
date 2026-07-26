module Fuaran.UI.Ops.Benchmarks.OpCorpus

open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Ops.Types

// ============================================================================
//  Op-stream benchmark corpus (Wave-T op-append follow-on) — representative
//  `TreeOp<unit>` values at three shapes, driving the op-stream encode + hash +
//  append path under measurement.
//
//  The Phase 200 `ApplyBenchmarks` measure apply / re-derivation only; the
//  op-stream WRITE path (canonical encode → SHA-256 hash-chain → durable append)
//  was never benchmarked, so the Phase 320 actor-in-hash change shipped without
//  a cost number. These shapes bracket the per-op write cost so the question
//  "is the larger hashed pre-image cheap?" gets an absolute answer (not a
//  before/after baseline — nothing is published yet).
//
//   - UpdateProp — a granular single-field edit. The most common AI op: a tiny
//                  encode + a short hash pre-image. The floor cost.
//   - InsertChild — a structural op carrying a markdown subtree. The encode now
//                  walks a whole `Node`, so the pre-image (and the SHA-256
//                  input) is materially larger than UpdateProp.
//   - Batch16    — a `Batch` of sixteen UpdateProps. The expensive case: one
//                  encode + one hash over a pre-image sixteen ops wide, the
//                  shape a multi-edit AI turn persists as a single record.
// ============================================================================

/// One op-stream scenario: a representative op at a named shape.
type Scenario = { Name: string; Op: TreeOp<unit> }

/// Boxed-value helper — mirrors `Corpus.v`: `PropValue.Native` wraps a non-null `obj`,
/// so the boxed payload is laundered through `Unchecked.nonNull` to type-check
/// under nullable reference types.
let private jv (x: obj | null) : PropValue = PropValue.Native(Unchecked.nonNull x)

/// A granular single-field property edit — the floor-cost op.
let private updatePropOp: TreeOp<unit> =
    TreeOp.UpdateProp(NodeId "field-3", "label", jv (box "Updated label"))

/// A structural insert carrying a markdown subtree — the encode walks a whole
/// `Node`, so the hash pre-image is materially larger than a granular edit.
let private insertChildOp: TreeOp<unit> =
    TreeOp.InsertChild(NodeId "root", Fuaran.markdown "inserted" "An inserted line of body copy.")

/// A batch of sixteen granular edits applied as one record — the wide-pre-image
/// case a multi-edit AI turn persists atomically.
let private batch16Op: TreeOp<unit> =
    TreeOp.Batch [ for i in 0..15 -> TreeOp.UpdateProp(NodeId $"field-{i}", "label", jv (box $"Updated label {i}")) ]

let updateProp =
    { Name = "UpdateProp"
      Op = updatePropOp }

let insertChild =
    { Name = "InsertChild"
      Op = insertChildOp }

let batch16 = { Name = "Batch16"; Op = batch16Op }

/// All three shapes, cheapest-first.
let all = [ updateProp; insertChild; batch16 ]
