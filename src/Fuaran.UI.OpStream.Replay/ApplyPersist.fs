namespace Fuaran.UI.OpStream.Replay

open System
open Fuaran.UI.Types
open Fuaran.UI.Ops
open Fuaran.UI.Ops.Types
open Fuaran.UI.OpStream.Abstractions
#if !FABLE_COMPILER
// System.Diagnostics.Stopwatch (the apply-timing seam) + the telemetry sink are
// .NET-only; the whole `applyWithSinks` fan-out below is guarded to match, so a
// Fable host can pull `Fuaran.UI.OpStream.Replay` (e.g. transitively via the
// Dag.Merge 3-way-merge engine) without the server-side timing/telemetry path.
open System.Diagnostics
open Fuaran.UI.Telemetry.Abstractions
#endif

// ============================================================================
//  applyAndPersist — apply-engine integration.
//
//  Wraps `Fuaran.UI.Ops.Apply.apply` to persist a hash-chained `OpRecord<'Msg>`
//  to an `IOpStreamSink<'Msg>` after a successful apply. Per the canonical
//  sketch in `docs/migrations/12-Z-op-stream.md` § "Apply-engine integration",
//  the apply path itself does NOT change shape — this is a DI seam, called
//  by hosts that want durable conversation-as-source-of-truth.
//
//  Design deviation from the migration doc's "integrates into Apply.fs at the
//  dispatch point" wording: the wrapper lives HERE rather than modifying
//  `Fuaran.UI.Ops.Apply.fs`. The dispatch-point form would force `Fuaran.UI.Ops`
//  to depend on `Fuaran.UI.OpStream.Abstractions` (for sink + HashChain), which
//  violates the §4l standalone posture mandate recorded in `Fuaran/CLAUDE.md`
//  ("`Fuaran.UI.Ops` ... Standalone — depends on `Fuaran.UI` + `FSharp.Core`
//  only"). Sitting the wrapper in OpStream.Replay — which already references
//  both Ops and OpStream.Abstractions for the read-back replay engine — keeps
//  the dependency direction clean.
//
//  Consequence: the `applyWithTelemetry` wrapper does NOT retire
//  alongside this work. Both single-sink wrappers still ship. Phase 124 adds
//  the both-sinks fan-out `applyWithSinks` (below): one apply that emits to
//  BOTH the op-stream sink and an `IFuaranTelemetrySink`, so durability and
//  telemetry are no longer mutually exclusive at the call site (FGP 5 — the
//  op-stream is the source of truth and telemetry observes the same seam).
//  The fan-out composes the two existing wrappers' logic WITHOUT modifying
//  `Fuaran.UI.Ops.Apply.fs`; the outcome→telemetry mapping is the shared
//  `OpOutcome.ofApplyResult` builder, so it cannot drift from
//  `applyWithTelemetry`.
//
//  Apply failure short-circuits without touching the sink (the v1
//  OpResultEnvelope.Failure case is reserved for the future
//  apply-failure-also-recorded variant). Sink failure does NOT block the
//  apply — durability is best-effort. Hosts that want strict durability
//  wrap their sink to propagate the throw.
//
//  Hash-chain previous hash is derived by querying the sink for the current
//  LatestSequence and Replay'ing the immediately-prior record; callers that
//  maintain their own sequence + hash cache can construct the OpRecord
//  directly and invoke `sink.Append` themselves — `applyAndPersist` is the
//  convenience for the common case.
// ============================================================================

/// Per-op correlation + sink-error context threaded into the persisted
/// `OpRecord`. The wrapper queries the sink for the next sequence and the
/// previous hash; the caller supplies only the stream identity, the user
/// id, and (optionally) the conversation's current prompt id.
type PersistContext =
    {
        StreamId: string
        UserId: string
        PromptId: string option
        /// Invoked synchronously inside the async block when `sink.Append`
        /// throws. Exceptions thrown by the callback itself are swallowed —
        /// the apply path is never broken by a misbehaving sink or logger.
        /// Default: no logging.
        OnSinkError: (exn -> unit) option
    }

module PersistContext =
    /// Minimal context with no PromptId and no sink-error logging hook.
    /// Equivalent to constructing the record with `PromptId = None` and
    /// `OnSinkError = None`.
    let create (streamId: string) (userId: string) : PersistContext =
        { StreamId = streamId
          UserId = userId
          PromptId = None
          OnSinkError = None }

    /// Attach a prompt id (conversation correlation) to the context.
    let withPromptId (promptId: string) (ctx: PersistContext) : PersistContext = { ctx with PromptId = Some promptId }

    /// Attach a sink-error logging hook to the context.
    let withSinkErrorHook (hook: exn -> unit) (ctx: PersistContext) : PersistContext =
        { ctx with OnSinkError = Some hook }

module ApplyPersist =

    let private currentTimestamp () : DateTimeOffset = DateTimeOffset.UtcNow

    /// Build the hash-chained `OpRecord` for an already-applied `op` at the
    /// given `sequence` and append it to `sink`. Returns the sequence used.
    /// Sink.Append failures are surfaced via `ctx.OnSinkError` (when set) but
    /// do NOT propagate — durability is best-effort. Shared by
    /// `applyAndPersist` and `applyWithSinks` so the persistence path (hash
    /// chain, previous-hash recovery, error handling) is identical between
    /// the single-sink and both-sinks wrappers (Phase 124).
    let private appendRecordAt<'Msg>
        (sink: IOpStreamSink<'Msg>)
        (ctx: PersistContext)
        (sequence: int)
        (op: TreeOp<'Msg>)
        : Async<unit> =
        async {
            let! previousHash =
                async {
                    if sequence = 1 then
                        return HashChain.genesisPreviousHash
                    else
                        let! prev = sink.Replay(ctx.StreamId, sequence - 1, sequence - 1)

                        match prev with
                        | r :: _ -> return r.Hash
                        | [] ->
                            // LatestSequence reported >0 but the prior record is
                            // missing — sink invariant violation. Best-effort: use
                            // the genesis hash. `Verify.chain` on the resulting
                            // stream will surface the gap as `OutOfOrder` /
                            // `PreviousHashMismatch`.
                            return HashChain.genesisPreviousHash
                }

            let timestamp = currentTimestamp ()
            // PersistContext keeps its bare-string UserId (host API unchanged);
            // lift it to a typed Human actor at the op-record boundary (Phase 320).
            let actor = Actor.ofLegacyString ctx.UserId
            // Phase 406: promptId + resultEnvelope are folded into the chain hash
            // (provenance is tamper-evident). v1 records only successful applies.
            let resultEnvelope = OpResultEnvelope.Success

            let hash =
                HashChain.computeHash previousHash op sequence timestamp actor ctx.PromptId resultEnvelope

            let record: OpRecord<'Msg> =
                { StreamId = ctx.StreamId
                  Sequence = sequence
                  PreviousHash = previousHash
                  Hash = hash
                  Op = op
                  PromptId = ctx.PromptId
                  Actor = actor
                  Timestamp = timestamp
                  ResultEnvelope = resultEnvelope }

            try
                do! sink.Append record
            with ex ->
                match ctx.OnSinkError with
                | Some hook ->
                    try
                        hook ex
                    with _ ->
                        ()
                | None -> ()
        }

    /// Apply `op` against `tree`. On `Ok`, persist a hash-chained `OpRecord`
    /// to `sink` and return the updated tree. On `Error`, return the apply
    /// error unchanged — the sink is not touched.
    ///
    /// Sink.Append failures are surfaced via `ctx.OnSinkError` (when set) but
    /// do NOT propagate — the apply path returns `Ok updated` regardless of
    /// sink durability. Callers that want strict durability wrap their sink
    /// in a synchronous variant that propagates throws.
    let applyAndPersist<'Msg>
        (sink: IOpStreamSink<'Msg>)
        (ctx: PersistContext)
        (op: TreeOp<'Msg>)
        (tree: Node<'Msg>)
        : Async<Result<Node<'Msg>, ApplyError>> =
        async {
            match Apply.apply op tree with
            | Error e -> return Error e
            | Ok updated ->
                let! latest = sink.LatestSequence ctx.StreamId
                do! appendRecordAt sink ctx (latest + 1) op
                return Ok updated
        }

    /// Journal an op that has ALREADY been applied — append-only, no re-apply.
    ///
    /// The Phase 193 in-page apply seam needs exactly this: the debug global's
    /// host-supplied `ApplyHandler` has already decoded, applied, and
    /// re-rendered by the time the op is handed over, so `applyAndPersist`
    /// would apply it a SECOND time against the already-updated tree. This
    /// appends the hash-chained record for the op that just happened, and
    /// nothing else.
    ///
    /// Chaining is delegated to the same private helper `applyAndPersist` uses,
    /// so there is exactly one place in the codebase that computes a record's
    /// `PreviousHash` / `Sequence` — a second implementation is how a stream
    /// silently mis-chains.
    let journalApplied<'Msg> (sink: IOpStreamSink<'Msg>) (ctx: PersistContext) (op: TreeOp<'Msg>) : Async<unit> =
        async {
            let! latest = sink.LatestSequence ctx.StreamId
            do! appendRecordAt sink ctx (latest + 1) op
        }

#if !FABLE_COMPILER
    /// Apply `op` against `tree` ONCE and fan out to BOTH sinks: persist a
    /// hash-chained `OpRecord` to the op-stream `sink` (on `Ok`) AND emit one
    /// `OpApplyTelemetry` to `telemetrySink` (on every outcome, success or
    /// failure). The recommended call site for hosts that want durability
    /// AND telemetry — `applyAndPersist` (op-stream only) and
    /// `applyWithTelemetry` (telemetry only) are not mutually exclusive any
    /// more (Phase 124, FGP 5).
    ///
    /// The op is applied exactly once (no double-apply): both records are
    /// derived from the single `Apply.apply` result. The telemetry `Sequence`
    /// equals the op-stream record's `Sequence` on success — the
    /// `(StreamId, Sequence)` join key — and the would-be next sequence on
    /// failure (no record is persisted on `Error`). Both sinks are
    /// best-effort: a telemetry throw is swallowed, and a persist failure is
    /// surfaced via `ctx.OnSinkError` without breaking the apply path.
    let applyWithSinks<'Msg>
        (sink: IOpStreamSink<'Msg>)
        (telemetrySink: IFuaranTelemetrySink)
        (ctx: PersistContext)
        (op: TreeOp<'Msg>)
        (tree: Node<'Msg>)
        : Async<Result<Node<'Msg>, ApplyError>> =
        async {
            let! latest = sink.LatestSequence ctx.StreamId
            let sequence = latest + 1

            let sw = Stopwatch.StartNew()
            let result = Apply.apply op tree
            sw.Stop()

            let telemetry: OpApplyTelemetry =
                { StreamId = ctx.StreamId
                  Sequence = sequence
                  OpKind = OpKind.ofTreeOp op
                  NodeId = OpApplyTelemetry.topLevelNodeId op
                  Outcome = OpOutcome.ofApplyResult result
                  TimeToApplyMs = sw.Elapsed.TotalMilliseconds
                  PromptId = ctx.PromptId
                  UserId = ctx.UserId
                  Timestamp = currentTimestamp () }

            try
                telemetrySink.RecordOpApply telemetry
            with _ ->
                // Telemetry is best-effort by contract; never let a sink
                // throw poison the apply + persist path.
                ()

            match result with
            | Error e -> return Error e
            | Ok updated ->
                do! appendRecordAt sink ctx sequence op
                return Ok updated
        }
#endif
