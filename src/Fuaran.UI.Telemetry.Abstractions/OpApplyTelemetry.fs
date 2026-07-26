namespace Fuaran.UI.Telemetry.Abstractions

open System
open Fuaran.UI.Types
open Fuaran.UI.Ops.Types

// ============================================================================
//  OpApplyTelemetry — one structured record per applied (or attempted) op.
//
//  Emitted from the apply-engine dispatch point (integration with
//  the applyAndPersist seam, or directly from the
//  `Fuaran.UI.Telemetry.Default.Apply.applyWithTelemetry` helper).
//
//  `OpOutcome` is closed and structural — host sinks can encode it without
//  a `'Msg`-aware codec. The four cases mirror the four `Result<_, ApplyError>`
//  branches the apply engine can take:
//
//   - Applied             — Result.Ok branch.
//   - DecoderRejected     — wire-shape decode failed before apply ran (rare
//                            in v1 since the typed apply takes typed
//                            input; reserved for the storage-shape
//                            follow-on where `Node<obj>` decode can fail).
//   - NodeNotFound        — ApplyError.Code in { NodeNotFound; ParentNotFound }.
//   - ApplyEngineError    — every other ApplyError (FieldNotFound,
//                            SlotNotFound, KindMismatch, ChildlessKind,
//                            PositionOutOfRange, OrderingMismatch,
//                            DuplicateNodeId, PathInvalid,
//                            PathNotSupportedYet, BatchAborted).
//
//  `(StreamId, Sequence)` correlates with `Fuaran.UI.OpStream.OpRecord` —
//  hosts that wire both sinks can join telemetry to the durable op record
//  via this composite key.
// ============================================================================

[<RequireQualifiedAccess>]
type OpOutcome =
    /// Apply engine returned Result.Ok; tree was updated.
    | Applied
    /// Wire-shape decode failed before the apply could run. Reserved for
    /// the storage-shape follow-on (`Node<obj>` + `moduleMsgDecoder`).
    /// `reason` is the host's decoder error message.
    | DecoderRejected of reason: string
    /// Apply engine returned Result.Error with code `NodeNotFound` or
    /// `ParentNotFound`. The drift detector counts top offending NodeIds
    /// to surface "AI is repeatedly targeting an absent node" patterns.
    | NodeNotFound of nodeId: string
    /// Apply engine returned Result.Error for any other reason.
    /// `detail` carries the `ApplyError.Code` discriminator name + the
    /// engine's `Message` for diagnostic context.
    | ApplyEngineError of detail: string

/// One op's worth of apply trace, surfaced to the configured
/// `IFuaranTelemetrySink` from the apply-engine dispatch point.
type OpApplyTelemetry =
    { StreamId: string
      Sequence: int
      OpKind: OpKind
      NodeId: string option
      Outcome: OpOutcome
      TimeToApplyMs: float
      PromptId: string option
      UserId: string
      Timestamp: DateTimeOffset }

[<RequireQualifiedAccess>]
module OpOutcome =

    let private errorCodeName (code: ApplyErrorCode) : string =
        match code with
        | ApplyErrorCode.NodeNotFound -> "NodeNotFound"
        | ApplyErrorCode.ParentNotFound -> "ParentNotFound"
        | ApplyErrorCode.FieldNotFound -> "FieldNotFound"
        | ApplyErrorCode.SlotNotFound -> "SlotNotFound"
        | ApplyErrorCode.KindMismatch -> "KindMismatch"
        | ApplyErrorCode.ChildlessKind -> "ChildlessKind"
        | ApplyErrorCode.PositionOutOfRange -> "PositionOutOfRange"
        | ApplyErrorCode.OrderingMismatch -> "OrderingMismatch"
        | ApplyErrorCode.DuplicateNodeId -> "DuplicateNodeId"
        | ApplyErrorCode.PathInvalid -> "PathInvalid"
        | ApplyErrorCode.PathNotSupportedYet -> "PathNotSupportedYet"
        | ApplyErrorCode.BatchAborted innerIndex -> sprintf "BatchAborted(%d)" innerIndex

    /// Derive the closed, `'Msg`-free `OpOutcome` from a typed apply result.
    /// This is the single source of truth for the apply-result → outcome
    /// mapping, shared by every apply wrapper that emits `OpApplyTelemetry`
    /// (`Telemetry.Default.Apply.applyWithTelemetry` and
    /// `OpStream.Replay.ApplyPersist.applyWithSinks`) so the mapping cannot
    /// drift between the parallel wrappers (Phase 124).
    let ofApplyResult (result: Result<Node<'Msg>, ApplyError>) : OpOutcome =
        match result with
        | Ok _ -> OpOutcome.Applied
        | Error err ->
            match err.Code with
            | ApplyErrorCode.NodeNotFound
            | ApplyErrorCode.ParentNotFound ->
                // Surface the offending NodeId so the drift detector can
                // count top-targets. `Message` carries the id formatted as
                // `Node 'X' not found in tree.` / `Parent node 'X' not found
                // in tree.` — extract it from the message for v1 rather than
                // threading a new field through every error builder.
                let parts = err.Message.Split([| '\'' |])
                let nodeId = if parts.Length >= 2 then parts[1] else "<unknown>"
                OpOutcome.NodeNotFound nodeId
            | code -> OpOutcome.ApplyEngineError(sprintf "%s: %s" (errorCodeName code) err.Message)

[<RequireQualifiedAccess>]
module OpApplyTelemetry =

    /// The primary `NodeId` a `TreeOp` targets — the edited/updated node, or
    /// the parent for child-positioned ops; `None` for `Batch` (no single
    /// target). Shared by the apply wrappers so the `NodeId` projection stays
    /// identical across them (Phase 124).
    let topLevelNodeId (op: TreeOp<'Msg>) : string option =
        match op with
        | TreeOp.EditNode(NodeId id, _) -> Some id
        | TreeOp.UpdateProp(NodeId id, _, _) -> Some id
        | TreeOp.ReplaceBinding(NodeId id, _, _) -> Some id
        | TreeOp.UpdateStyle(NodeId id, _) -> Some id
        | TreeOp.UpdateState(NodeId id, _) -> Some id
        | TreeOp.InsertChild(NodeId parentId, _) -> Some parentId
        | TreeOp.RemoveNode(NodeId id) -> Some id
        | TreeOp.MoveNode(NodeId id, _) -> Some id
        | TreeOp.ReorderChildren(NodeId parentId, _) -> Some parentId
        | TreeOp.ReplaceRoot node -> let (NodeId rawId) = node.Id in Some rawId
        | TreeOp.Batch _ -> None
