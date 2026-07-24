module Fuaran.UI.Telemetry.Default.Apply

open System
open System.Diagnostics
open Fuaran.UI.Types
open Fuaran.UI.Ops
open Fuaran.UI.Ops.Types
open Fuaran.UI.Telemetry.Abstractions

// ============================================================================
//  Apply-engine telemetry wrapper.
//
//  The apply-engine integration ships as an opt-in WRAPPER
//  around `Fuaran.UI.Ops.Apply.apply` rather than as a modification to
//  `apply` itself. Two reasons (one originally three — see history note):
//
//   1. `Fuaran.UI.Ops` stays sink-unaware. The apply engine's
//      `apply : TreeOp<'Msg> -> Node<'Msg> -> Result<...>` signature
//      remains the standalone-able shape the §4l down-shift portability
//      story requires.
//
//   2. Hosts that don't want telemetry pay nothing — they call
//      `Fuaran.UI.Ops.Apply.apply` directly.
//
//  History note: an earlier docstring revision claimed this wrapper would
//  "retire" once the `applyAndPersist` integration landed and wired
//  both sinks at a single `Apply.fs` dispatch point. That retirement does
//  NOT happen — when the integration shipped, it also
//  landed as a wrapper (`Fuaran.UI.OpStream.Replay.ApplyPersist.applyAndPersist`)
//  for the same standalone-posture reason in (1). Both single-sink wrappers
//  still ship; Phase 124 adds `OpStream.Replay.ApplyPersist.applyWithSinks`,
//  the both-sinks fan-out a host should call when it wants durability AND
//  telemetry from one apply (so the two are no longer mutually exclusive at
//  the call site). See `docs/migrations/12-Z-op-stream.md` § "Design deviation
//  from the original sketch" for the parallel-wrappers verdict.
//
//  The wrapper's outcome-derivation mirrors what the Apply.fs dispatch
//  point would do once integrated:
//    Result.Ok _                              -> OpOutcome.Applied
//    ApplyErrorCode.NodeNotFound | ParentNotFound  -> OpOutcome.NodeNotFound
//    Every other ApplyErrorCode             -> OpOutcome.ApplyEngineError
//  `OpOutcome.DecoderRejected` is reserved for the storage-shape follow-on
//  (`Node<obj>` decode failure) and is not produced by the typed apply.
// ============================================================================

/// Per-op correlation context the wrapper threads from the caller into
/// the emitted telemetry record. Lifted into a record so hosts that
/// dispatch many ops in a row pass a single value through.
type ApplyContext =
    { StreamId: string
      Sequence: int
      UserId: string
      PromptId: string option }

// The apply-result → `OpOutcome` mapping and the `topLevelNodeId` projection
// now live in `Fuaran.UI.Telemetry.Abstractions` (`OpOutcome.ofApplyResult` /
// `OpApplyTelemetry.topLevelNodeId`) as the single source of truth shared with
// `OpStream.Replay.ApplyPersist.applyWithSinks`, so the two parallel apply
// wrappers cannot drift in how they classify an outcome (Phase 124).

/// Apply `op` against `tree`, emit one `OpApplyTelemetry` to `sink`
/// regardless of outcome, and return the apply result unchanged. The
/// sink call is sync fire-and-forget — exceptions thrown by the sink
/// are swallowed so the apply path is never broken by a misbehaving
/// telemetry implementation. The result envelope passed to the caller
/// is the exact `Result<Node<'Msg>, ApplyError>` `Fuaran.UI.Ops.Apply.apply`
/// returned.
let applyWithTelemetry
    (sink: IFuaranTelemetrySink)
    (ctx: ApplyContext)
    (op: TreeOp<'Msg>)
    (tree: Node<'Msg>)
    : Result<Node<'Msg>, ApplyError> =
    let sw = Stopwatch.StartNew()
    let result = Apply.apply op tree
    sw.Stop()

    let telemetry =
        { StreamId = ctx.StreamId
          Sequence = ctx.Sequence
          OpKind = OpKind.ofTreeOp op
          NodeId = OpApplyTelemetry.topLevelNodeId op
          Outcome = OpOutcome.ofApplyResult result
          TimeToApplyMs = sw.Elapsed.TotalMilliseconds
          PromptId = ctx.PromptId
          UserId = ctx.UserId
          Timestamp = DateTimeOffset.UtcNow }

    try
        sink.RecordOpApply telemetry
    with _ ->
        // Telemetry is best-effort; never let a sink throw poison the
        // apply path. Hosts that want strict telemetry wrap their sink
        // in a synchronous variant that propagates internally.
        ()

    result
