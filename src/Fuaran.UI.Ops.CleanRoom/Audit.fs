module Fuaran.UI.Ops.CleanRoom.Audit

// ============================================================================
//  Structure-only clean room — audit emission (FGP 5).
//
//  The clean-room divide must be auditable end to end: every issued skeleton
//  and every Released / Withheld gate decision is recorded, so an operator (or
//  a security reviewer) can reconstruct exactly what crossed the divide and
//  what the gate did with it — the same peer-audit transparency a platform
//  host's inter-platform substrate provides on the transport side, here on the
//  enforcement side.
//
//  The audit records are themselves CONTENT-FREE: a skeleton issuance records
//  the root id + node *count* (a number, never content); a gate decision
//  records the op's structural kind, the ids it referenced (structural, not
//  prose), and the outcome. Nothing prose-shaped reaches the sink.
//
//  Purity boundary: the broker (`Broker.enforce`) stays a pure function of
//  (skeleton, op) — audit emission is THIS seam, not baked into the gate. The
//  `issue` / `enforceAudited` helpers compose projection + enforcement with
//  emission so a host gets auditability without the gate carrying a sink.
//  A host wires `ICleanRoomAuditSink` to its op-stream / telemetry sink.
// ============================================================================

open Fuaran.UI.Types
open Fuaran.UI.Ops.Types
open Fuaran.UI.Ops.CleanRoom.Skeleton
open Fuaran.UI.Ops.CleanRoom.Broker

// ─── Content-free audit records ─────────────────────────────────────────────

/// Audit record for one issued skeleton. `NodeCount` is a count, never
/// content; `RootId` is the structural tree root id.
type SkeletonIssued = { RootId: NodeId; NodeCount: int }

/// The outcome of a gate decision, for audit. `Withheld` carries the
/// structured reason (which floor the op breached) — never node content.
[<RequireQualifiedAccess>]
type GateOutcome =
    | Released
    | Withheld of reason: string

/// Audit record for one gate decision. `OpKind` is the op's structural
/// discriminator (e.g. `"MoveNode"`); `TargetIds` are the structural ids the
/// op referenced; `Outcome` is the Released / Withheld verdict. Content-free
/// by construction.
type GateDecisionRecord =
    { OpKind: string
      TargetIds: NodeId list
      Outcome: GateOutcome }

/// The audit seam. Both methods are sync fire-and-forget (mirroring
/// `IFuaranTelemetrySink`'s best-effort contract) — a host adapts this to its
/// durable op-stream / telemetry sink.
type ICleanRoomAuditSink =
    /// Record that a content-free skeleton was issued across the divide.
    abstract member RecordSkeletonIssued: issued: SkeletonIssued -> unit
    /// Record one Released / Withheld gate decision over an inbound op.
    abstract member RecordGateDecision: decision: GateDecisionRecord -> unit

// ─── Concrete sinks ─────────────────────────────────────────────────────────

/// No-op sink (the default when a deployment does not wire audit). Pay-for-
/// what-you-use: a caller that does not need the audit trail uses this.
type NoOpCleanRoomAuditSink() =
    interface ICleanRoomAuditSink with
        member _.RecordSkeletonIssued _ = ()
        member _.RecordGateDecision _ = ()

/// In-memory sink — buffers issuances + decisions for inspection (tests, a
/// host's diagnostics surface). Not durable; a host wires a durable sink.
type InMemoryCleanRoomAuditSink() =
    let issued = ResizeArray<SkeletonIssued>()
    let decisions = ResizeArray<GateDecisionRecord>()

    /// Skeletons issued so far, in emission order.
    member _.Issued: SkeletonIssued list = List.ofSeq issued
    /// Gate decisions recorded so far, in emission order.
    member _.Decisions: GateDecisionRecord list = List.ofSeq decisions

    interface ICleanRoomAuditSink with
        member _.RecordSkeletonIssued e = issued.Add e
        member _.RecordGateDecision e = decisions.Add e

// ─── Op metadata extraction (structural, content-free) ──────────────────────

/// The structural discriminator of a `TreeOp` (for the audit record).
let opKindName (op: TreeOp<'Msg>) : string =
    match op with
    | TreeOp.EditNode _ -> "EditNode"
    | TreeOp.UpdateProp _ -> "UpdateProp"
    | TreeOp.ReplaceBinding _ -> "ReplaceBinding"
    | TreeOp.UpdateStyle _ -> "UpdateStyle"
    | TreeOp.UpdateState _ -> "UpdateState"
    | TreeOp.InsertChild _ -> "InsertChild"
    | TreeOp.RemoveNode _ -> "RemoveNode"
    | TreeOp.MoveNode _ -> "MoveNode"
    | TreeOp.ReorderChildren _ -> "ReorderChildren"
    | TreeOp.ReplaceRoot _ -> "ReplaceRoot"
    | TreeOp.Batch _ -> "Batch"

/// The structural `NodeId`s an op references — never its content. Used for the
/// audit record so the divide is traceable by id without exposing prose. An
/// `InsertChild`'s new child contributes only its parent target (the inserted
/// subtree's content is never read).
let rec referencedIds (op: TreeOp<'Msg>) : NodeId list =
    match op with
    | TreeOp.EditNode(id, _) -> [ id ]
    | TreeOp.UpdateProp(id, _, _) -> [ id ]
    | TreeOp.ReplaceBinding(id, _, _) -> [ id ]
    | TreeOp.UpdateStyle(id, _) -> [ id ]
    | TreeOp.UpdateState(id, _) -> [ id ]
    | TreeOp.InsertChild(parentId, _, _) -> [ parentId ]
    | TreeOp.RemoveNode id -> [ id ]
    | TreeOp.MoveNode(id, newParentId, _) -> [ id; newParentId ]
    | TreeOp.ReorderChildren(parentId, newOrder) -> parentId :: newOrder
    | TreeOp.ReplaceRoot node -> [ node.Id ]
    | TreeOp.Batch inner -> inner |> List.collect referencedIds

// ─── Audited orchestration ──────────────────────────────────────────────────

/// Project a tree to a content-free skeleton and emit the issuance to `sink`.
/// The default domain-neutral descriptor; for a domain classifier use
/// `issueWith`.
let issue (sink: ICleanRoomAuditSink) (tree: Node<'Msg>) : Skeleton =
    let skeleton = project tree

    sink.RecordSkeletonIssued
        { RootId = skeleton.Root.Id
          NodeCount = nodeCount skeleton }

    skeleton

/// Project a tree with a caller-supplied descriptor classifier, emitting the
/// issuance to `sink`.
let issueWith (sink: ICleanRoomAuditSink) (classify: Classify<'Msg>) (tree: Node<'Msg>) : Skeleton =
    let skeleton = projectWith classify tree

    sink.RecordSkeletonIssued
        { RootId = skeleton.Root.Id
          NodeCount = nodeCount skeleton }

    skeleton

/// Enforce an inbound op through `broker`, emitting the Released / Withheld
/// decision to `sink`. The broker itself stays pure; this composes the pure
/// gate with audit emission.
let enforceAudited
    (sink: ICleanRoomAuditSink)
    (broker: IStructuralOpBroker)
    (skeleton: Skeleton)
    (op: TreeOp<'Msg>)
    : StructuralGateDecision<'Msg> =
    let decision = broker.Enforce(skeleton, op)

    let outcome =
        match decision with
        | StructuralGateDecision.Released _ -> GateOutcome.Released
        | StructuralGateDecision.Withheld reason -> GateOutcome.Withheld reason

    sink.RecordGateDecision
        { OpKind = opKindName op
          TargetIds = referencedIds op
          Outcome = outcome }

    decision
