module Fuaran.UI.Ops.CleanRoom.Broker

// ============================================================================
//  Structure-only clean room — the structural-op gate.
//
//  `StructuralOpBroker` is the structure-only counterpart of a platform
//  host's aggregate clean-room broker: the SAME divide, a DIFFERENT enforcement
//  predicate. Where the aggregate broker validates an outbound *answer*
//  against a k-anonymity floor, this broker validates an INBOUND `TreeOp`
//  (authored by the untrusted side against the skeleton it was issued)
//  against the structure-only floor, returning the same pure, audit-friendly
//  `Released` / `Withheld reason` decision shape.
//
//  The structure-only floor, by construction:
//   1. Id-grounding — every `NodeId` the op references must exist in the
//      issued skeleton. An op targeting an unknown id is a fabricated /
//      smuggled target → withheld.
//   2. No content authoring — only id-referenced rearrangements survive
//      (RemoveNode / MoveNode / ReorderChildren / a Batch of those). Any op
//      that sets, carries, or inserts content (EditNode / UpdateProp /
//      ReplaceBinding / UpdateState / InsertChild / ReplaceRoot) authors text
//      and is withheld.
//   3. Structural allowlist — anything not on the move / reorder / reparent /
//      delete allowlist (e.g. UpdateStyle) is withheld by default-deny.
//
//  Everything that survives is, by construction, a content-free id-referenced
//  rearrangement: applying it on the trusted side re-derives the real document
//  (the determinism proof in the test suite). The guarantee transfers exactly
//  from the aggregate broker — "I sent a content-free skeleton and got back
//  only id-referenced structural moves" is guaranteed by the gate, not by
//  trusting the model.
//
//  Why a sibling seam (`IStructuralOpBroker`) rather than a platform host's
//  literal clean-room broker: such a broker's `GateDecision.Released` carries a
//  `CohortResult` (a k-anonymous aggregate), which has no meaning for a
//  structural op. So this package mirrors the broker's *shape* — a substitutable
//  interface + a pure, stateless `Enforce` returning `Released` / `Withheld
//  reason` — over the `TreeOp` payload. A deployment swaps a different structural
//  floor in behind `IStructuralOpBroker` exactly as a platform host documents for
//  its clean-room broker.
//
//  GP 12 rule 4 — the broker holds NO state between calls; `Enforce` is a pure
//  function of (skeleton, op). Audit emission is a separate seam (`Audit.fs`)
//  so the gate itself stays pure.
// ============================================================================

open Fuaran.UI.Types
open Fuaran.UI.Ops.Types
open Fuaran.UI.Ops.CleanRoom.Skeleton

/// The broker's decision over an inbound `TreeOp` — the structure-only mirror
/// of a platform host's aggregate `GateDecision`. `Released` carries the op the trusted side may
/// replay; `Withheld` carries a structured, audit-friendly reason.
[<RequireQualifiedAccess>]
type StructuralGateDecision<'Msg> =
    /// The op cleared the structure-only floor: it is a content-free,
    /// id-grounded rearrangement the trusted side may apply.
    | Released of op: TreeOp<'Msg>
    /// The op was withheld. `Reason` explains which floor it breached
    /// (unknown id / content authoring / off the structural allowlist).
    | Withheld of reason: string

/// The substitutable structural-op gate seam (the structure-only analogue of
/// a platform host's clean-room broker). A deployment that needs a different structural
/// floor substitutes its own implementation behind this interface without
/// changing call sites.
type IStructuralOpBroker =
    /// Validate an inbound `TreeOp` against the issued `Skeleton`. Pure and
    /// stateless — the decision is a function of (skeleton, op) only.
    abstract member Enforce<'Msg> : skeleton: Skeleton * op: TreeOp<'Msg> -> StructuralGateDecision<'Msg>

// ─── Reason builders ────────────────────────────────────────────────────────

let private unknownId (NodeId raw) : string =
    sprintf "op references NodeId '%s', which is not present in the issued skeleton" raw

let private contentReason (opName: string) : string =
    sprintf
        "op '%s' authors / carries content; the structure-only floor permits id-referenced move/reorder/reparent/delete only"
        opName

let private allowlistReason (opName: string) : string =
    sprintf "op '%s' is not on the structural allowlist (move/reorder/reparent/delete)" opName

// ─── Enforcement (pure) ─────────────────────────────────────────────────────

/// Validate `op` against `skeleton`'s known-id set. Pure; the public entry
/// point behind `IStructuralOpBroker.Enforce`.
let enforce (skeleton: Skeleton) (op: TreeOp<'Msg>) : StructuralGateDecision<'Msg> =
    let known = knownIds skeleton

    let grounded (id: NodeId) = Set.contains id known

    let rec go (op: TreeOp<'Msg>) : StructuralGateDecision<'Msg> =
        match op with
        // ── Allowed: id-referenced rearrangements (content-free) ──
        | TreeOp.RemoveNode target ->
            if grounded target then
                StructuralGateDecision.Released op
            else
                StructuralGateDecision.Withheld(unknownId target)
        | TreeOp.MoveNode(target, newParentId) ->
            if not (grounded target) then
                StructuralGateDecision.Withheld(unknownId target)
            elif not (grounded newParentId) then
                StructuralGateDecision.Withheld(unknownId newParentId)
            else
                StructuralGateDecision.Released op
        | TreeOp.ReorderChildren(parentId, newOrder) ->
            if not (grounded parentId) then
                StructuralGateDecision.Withheld(unknownId parentId)
            else
                match newOrder |> List.tryFind (grounded >> not) with
                | Some ungrounded -> StructuralGateDecision.Withheld(unknownId ungrounded)
                | None -> StructuralGateDecision.Released op
        | TreeOp.Batch inner ->
            // All-or-nothing: the batch releases only if every inner op clears
            // the floor; the first withheld inner op withholds the whole batch
            // (and its reason carries the inner index for audit).
            let rec loop idx remaining =
                match remaining with
                | [] -> StructuralGateDecision.Released op
                | next :: rest ->
                    match go next with
                    | StructuralGateDecision.Released _ -> loop (idx + 1) rest
                    | StructuralGateDecision.Withheld reason ->
                        StructuralGateDecision.Withheld(sprintf "batch inner op #%d withheld: %s" idx reason)

            loop 0 inner
        // ── Withheld: content-authoring ops ──
        | TreeOp.EditNode _ -> StructuralGateDecision.Withheld(contentReason "EditNode")
        | TreeOp.UpdateProp _ -> StructuralGateDecision.Withheld(contentReason "UpdateProp")
        | TreeOp.ReplaceBinding _ -> StructuralGateDecision.Withheld(contentReason "ReplaceBinding")
        | TreeOp.UpdateState _ -> StructuralGateDecision.Withheld(contentReason "UpdateState")
        | TreeOp.InsertChild _ -> StructuralGateDecision.Withheld(contentReason "InsertChild")
        | TreeOp.ReplaceRoot _ -> StructuralGateDecision.Withheld(contentReason "ReplaceRoot")
        // ── Withheld: off the structural allowlist (default-deny) ──
        | TreeOp.UpdateStyle _ -> StructuralGateDecision.Withheld(allowlistReason "UpdateStyle")

    go op

/// Default structure-only broker: pure, stateless (GP 12 rule 4). Substitute a
/// different structural floor behind `IStructuralOpBroker` without changing
/// call sites.
type StructuralOpBroker() =
    interface IStructuralOpBroker with
        member _.Enforce<'Msg>(skeleton: Skeleton, op: TreeOp<'Msg>) : StructuralGateDecision<'Msg> =
            enforce skeleton op

[<RequireQualifiedAccess>]
module StructuralOpBroker =
    /// Construct the default broker behind the `IStructuralOpBroker` seam.
    let create () : IStructuralOpBroker =
        StructuralOpBroker() :> IStructuralOpBroker
