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
/// `OpRecord`. The wrapper allocates the record's position against the sink's
/// head; the caller supplies the stream identity, who is acting, and
/// (optionally) the conversation's current prompt id.
type PersistContext =
    {
        StreamId: string
        UserId: string
        PromptId: string option
        /// The typed actor to record (Phase 1525, finding M-C4).
        ///
        /// `None` means "derive it from `UserId`", which is what every record
        /// written before this field existed did — so an existing caller is
        /// unaffected and its records are byte-identical. A host that already
        /// knows the actor is an agent, a merge, or a replay says so HERE, and
        /// the wrapper does not overwrite it: `Actor.ofLegacyString` is applied
        /// only when this is absent. Before this field there was no way to
        /// record anything but a `Human`, whatever was actually acting.
        Actor: Actor option
        /// Invoked synchronously inside the async block when an append fails or
        /// is lost. Exceptions thrown by the callback itself are swallowed —
        /// the apply path is never broken by a misbehaving sink or logger.
        ///
        /// **`None` is no longer silence** (Phase 1525, finding H-15). It means
        /// `PersistFailure.defaultReport`, which writes one line to stderr
        /// naming the stream, the sequence and the reason. A lost durable op
        /// that nothing anywhere records is the failure this phase exists to
        /// remove, and a default of silence is how it kept happening. A host
        /// that genuinely wants no report says so by name —
        /// `PersistContext.withSilentSinkErrors`.
        OnSinkError: (exn -> unit) option
    }

/// Why a persist attempt did not become durable — the value the retry loop
/// hands back and the sink-error channel reports (Phase 1525).
[<RequireQualifiedAccess>]
type PersistFailure =
    /// The compare-and-append kept losing to a concurrent writer until the
    /// attempt budget ran out. Carries the number of attempts made.
    | ContendedOut of attempts: int
    /// The sink refused or failed the append. Carries the sink's own message.
    | SinkRefused of reason: string

module PersistFailure =

    /// A one-line account of a failure, naming the stream and the sequence the
    /// attempt was for.
    let describe (streamId: string) (sequence: int) (failure: PersistFailure) : string =
        match failure with
        | PersistFailure.ContendedOut attempts ->
            sprintf
                "op-stream append to '%s' at sequence %d was NOT persisted: lost the compare-and-append %d times to a concurrent writer and gave up. The op was applied; it is not durable."
                streamId
                sequence
                attempts
        | PersistFailure.SinkRefused reason ->
            sprintf
                "op-stream append to '%s' at sequence %d was NOT persisted: %s. The op was applied; it is not durable."
                streamId
                sequence
                reason

    /// The report a context with no hook makes. Deliberately stderr and
    /// deliberately unconditional: this package takes no logging dependency
    /// (FGP 2 — it compiles under Fable, where this is `console.error`), and the
    /// alternative it replaces is a lost durable op that nothing records at all.
    let defaultReport (message: string) : unit = eprintfn "%s" message

/// Carries a `PersistFailure` through the `exn`-shaped `OnSinkError` channel
/// without losing which failure it was. A hook that only logs sees a sensible
/// `Message`; a hook that wants the typed value pattern-matches on it.
exception PersistFailedException of streamId: string * sequence: int * failure: PersistFailure

module PersistContext =
    /// Minimal context with no PromptId, the actor derived from `userId`, and
    /// the DEFAULT sink-error report (not silence — see `OnSinkError`).
    let create (streamId: string) (userId: string) : PersistContext =
        { StreamId = streamId
          UserId = userId
          PromptId = None
          Actor = None
          OnSinkError = None }

    /// Attach a prompt id (conversation correlation) to the context.
    let withPromptId (promptId: string) (ctx: PersistContext) : PersistContext = { ctx with PromptId = Some promptId }

    /// Record a specific typed actor rather than deriving a `Human` from
    /// `UserId` (Phase 1525). This is how an agent, a merge or a replay reaches
    /// the record as what it is.
    let withActor (actor: Actor) (ctx: PersistContext) : PersistContext = { ctx with Actor = Some actor }

    /// Attach a sink-error logging hook to the context.
    let withSinkErrorHook (hook: exn -> unit) (ctx: PersistContext) : PersistContext =
        { ctx with OnSinkError = Some hook }

    /// Suppress the default report. The ONLY way to get silence on a lost
    /// append, and it has to be said out loud — the same shape as
    /// `LoadVerification.Off` and `WriteAdmission.Off`.
    let withSilentSinkErrors (ctx: PersistContext) : PersistContext =
        { ctx with
            OnSinkError = Some(fun _ -> ()) }

/// What one persist attempt produced. Public because both the fan-out wrappers
/// and a host that wants to know whether its op became durable read it.
[<RequireQualifiedAccess>]
type PersistAttempt =
    /// The record is in the stream at this sequence.
    | Persisted of sequence: int
    /// The record is NOT in the stream. The sequence is the address the attempt
    /// was for.
    | Failed of sequence: int * failure: PersistFailure

module ApplyPersist =

    /// How many times the compare-and-append rebuilds against a moved head
    /// before giving up. Bounded on purpose: an unbounded retry against a
    /// permanently faster writer is a livelock that looks like a hang, and the
    /// honest answer after a bounded number of genuine losses is to say the op
    /// was not persisted rather than to keep trying for ever. Eight is well past
    /// what real contention produces and well short of anything a caller waits
    /// on.
    [<Literal>]
    let MaxCasAttempts = 8

    let private currentTimestamp () : DateTimeOffset = DateTimeOffset.UtcNow

    /// Surface a failure through `ctx.OnSinkError`, or through the default
    /// report when the context set no hook. Never propagates — durability is
    /// best-effort by contract, and a hook that throws must not break the apply
    /// path either.
    let private reportFailure (ctx: PersistContext) (sequence: int) (failure: PersistFailure) : unit =
        match ctx.OnSinkError with
        | Some hook ->
            try
                hook (PersistFailedException(ctx.StreamId, sequence, failure))
            with _ ->
                ()
        | None ->
            try
                PersistFailure.defaultReport (PersistFailure.describe ctx.StreamId sequence failure)
            with _ ->
                ()

    /// The actor to record: the context's own when it named one, otherwise the
    /// legacy lift of `UserId` (Phase 1525 — lifted only when absent).
    let private actorOf (ctx: PersistContext) : Actor =
        match ctx.Actor with
        | Some actor -> actor
        | None -> Actor.ofLegacyString ctx.UserId

    /// Build the hash-chained `OpRecord` for an already-applied `op` at an
    /// EXPLICIT position — the caller supplies both the sequence and the
    /// previous hash it read from the store.
    ///
    /// The single place a record's chain fields are assembled. Splitting the
    /// POSITION out of the build (Phase 1525) is what lets the compare-and-append
    /// below rebuild a record against a head it has just been told about,
    /// without a second round trip to rediscover it.
    let private buildRecordWith<'Msg>
        (ctx: PersistContext)
        (sequence: int)
        (previousHash: string)
        (op: TreeOp<'Msg>)
        : OpRecord<'Msg> =
        let timestamp = currentTimestamp ()
        let actor = actorOf ctx
        // Phase 406: promptId + resultEnvelope are folded into the chain hash,
        // so provenance is covered by the digest (corruption detection — the
        // chain is unkeyed; see CRYPTO.md). v1 records only successful applies.
        let resultEnvelope = OpResultEnvelope.Success

        let hash =
            HashChain.computeHash previousHash op sequence timestamp actor ctx.PromptId resultEnvelope

        { StreamId = ctx.StreamId
          Sequence = sequence
          PreviousHash = previousHash
          Hash = hash
          Op = op
          PromptId = ctx.PromptId
          Actor = actor
          Timestamp = timestamp
          ResultEnvelope = resultEnvelope }

    /// Recover the previous hash for a record at `sequence` by reading the
    /// record before it. The non-compare-and-append path only — a sink that
    /// implements `IOpStreamCasSink` reports its head directly and never needs
    /// this.
    let private previousHashAt<'Msg>
        (sink: IOpStreamSink<'Msg>)
        (ctx: PersistContext)
        (sequence: int)
        : Async<Result<string, string>> =
        async {
            if sequence = 1 then
                return Ok HashChain.genesisPreviousHash
            else
                let! prev = sink.Replay(ctx.StreamId, sequence - 1, sequence - 1)

                match prev with
                | r :: _ -> return Ok r.Hash
                | [] ->
                    // `LatestSequence` reported > 0 and the record before this
                    // one is missing. Phase 1525: this is a GAP, and the
                    // pre-1525 code papered over it with the genesis hash —
                    // writing a record whose `PreviousHash` names nothing, which
                    // then made every later `Replay` of the segment throw. A
                    // refusal here costs one op; the paper-over cost the stream.
                    return
                        Error(
                            sprintf
                                "the record at sequence %d is missing, so the record at %d has no head to link to"
                                (sequence - 1)
                                sequence
                        )
        }

    /// Build the hash-chained `OpRecord` for an already-applied `op` at the
    /// given `sequence`, recovering the previous hash from `sink`. The non-CAS
    /// path's builder; the CAS path uses `buildRecordWith` against the head the
    /// sink reported.
    let private buildRecordAt<'Msg>
        (sink: IOpStreamSink<'Msg>)
        (ctx: PersistContext)
        (sequence: int)
        (op: TreeOp<'Msg>)
        : Async<Result<OpRecord<'Msg>, string>> =
        async {
            let! previousHash = previousHashAt sink ctx sequence
            return previousHash |> Result.map (fun h -> buildRecordWith ctx sequence h op)
        }

    /// Persist `op` — through the sink's compare-and-append when it has one.
    ///
    /// **The loop, and why it is shaped this way.** Each attempt reads the HEAD
    /// FIRST and the latest sequence SECOND, then builds against both and calls
    /// `AppendIf` with the head it read. The ORDER IS LOAD-BEARING: a write that
    /// lands between the two reads moves the head *and* the sequence, and taking
    /// the head first means the record is built against a head the store has
    /// already left — which `AppendIf` reports as `StaleHead`, the value this
    /// loop knows how to handle. Taking the sequence first would produce the
    /// opposite pairing (a current head with a stale sequence), which passes the
    /// head comparison and is then refused by the sink's admission check as a
    /// throw — a race reported as corruption.
    ///
    /// A sink with no compare-and-append keeps the read-then-append path. That
    /// path is genuinely racy and always was; what changes is that its loss is
    /// now REPORTED rather than swallowed, so a host on such a sink can see the
    /// cost of the sink it chose.
    let private persistOp<'Msg>
        (sink: IOpStreamSink<'Msg>)
        (ctx: PersistContext)
        (op: TreeOp<'Msg>)
        : Async<PersistAttempt> =
        async {
            match sink with
            | :? IOpStreamCasSink<'Msg> as cas ->
                let mutable attempt = 0
                let mutable outcome = ValueNone

                while outcome.IsNone && attempt < MaxCasAttempts do
                    attempt <- attempt + 1
                    let! head = cas.Head ctx.StreamId
                    let! latest = sink.LatestSequence ctx.StreamId
                    let sequence = latest + 1
                    let record = buildRecordWith ctx sequence head op

                    let! result =
                        async {
                            try
                                let! r = cas.AppendIf(record, head)
                                return Choice1Of2 r
                            with ex ->
                                return Choice2Of2 ex
                        }

                    match result with
                    | Choice1Of2(CasAppendOutcome.Appended receipt) ->
                        outcome <- ValueSome(PersistAttempt.Persisted receipt.Sequence)
                    | Choice1Of2(CasAppendOutcome.StaleHead _) ->
                        // Another writer got there first. Rebuild against what
                        // the store now holds and try again — that is the whole
                        // point of a compare-and-append, and it is why the
                        // record is built INSIDE the loop.
                        ()
                    | Choice2Of2 ex ->
                        outcome <- ValueSome(PersistAttempt.Failed(sequence, PersistFailure.SinkRefused ex.Message))

                match outcome with
                | ValueSome result -> return result
                | ValueNone ->
                    let! latest = sink.LatestSequence ctx.StreamId
                    return PersistAttempt.Failed(latest + 1, PersistFailure.ContendedOut MaxCasAttempts)
            | _ ->
                let! latest = sink.LatestSequence ctx.StreamId
                let sequence = latest + 1
                let! built = buildRecordAt sink ctx sequence op

                match built with
                | Error reason -> return PersistAttempt.Failed(sequence, PersistFailure.SinkRefused reason)
                | Ok record ->
                    try
                        do! sink.Append record
                        return PersistAttempt.Persisted sequence
                    with ex ->
                        return PersistAttempt.Failed(sequence, PersistFailure.SinkRefused ex.Message)
        }

    /// Persist `op` and report a loss through `ctx.OnSinkError` (or the default
    /// report). Returns the outcome so the telemetry fan-out below can NAME it
    /// rather than assume it.
    let private persistAndReport<'Msg>
        (sink: IOpStreamSink<'Msg>)
        (ctx: PersistContext)
        (op: TreeOp<'Msg>)
        : Async<PersistAttempt> =
        async {
            let! attempt = persistOp sink ctx op

            match attempt with
            | PersistAttempt.Failed(sequence, failure) -> reportFailure ctx sequence failure
            | PersistAttempt.Persisted _ -> ()

            return attempt
        }

    /// Apply `op` against `tree`. On `Ok`, persist a hash-chained `OpRecord`
    /// to `sink` and return the updated tree. On `Error`, return the apply
    /// error unchanged — the sink is not touched.
    ///
    /// The append goes through the sink's compare-and-append when it has one,
    /// with bounded retry (Phase 1525) — so two concurrent writers on one stream
    /// both persist, at contiguous sequences, instead of one silently losing to
    /// the other's duplicate-sequence refusal.
    ///
    /// A persist failure does NOT propagate — the apply path returns
    /// `Ok updated` regardless of durability — but it is no longer SILENT: it
    /// reaches `ctx.OnSinkError`, or the default stderr report when the context
    /// set no hook. Callers that want strict durability wrap their sink in a
    /// synchronous variant that propagates throws, or call `applyAndPersistWith`
    /// below and read the attempt.
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
                let! _attempt = persistAndReport sink ctx op
                return Ok updated
        }

    /// `applyAndPersist` that RETURNS the persist outcome beside the tree
    /// (Phase 1525) — for a host that needs to know whether its op became
    /// durable rather than being told about it through a callback. The
    /// behaviour is otherwise identical, reporting included.
    let applyAndPersistWith<'Msg>
        (sink: IOpStreamSink<'Msg>)
        (ctx: PersistContext)
        (op: TreeOp<'Msg>)
        (tree: Node<'Msg>)
        : Async<Result<Node<'Msg> * PersistAttempt, ApplyError>> =
        async {
            match Apply.apply op tree with
            | Error e -> return Error e
            | Ok updated ->
                let! attempt = persistAndReport sink ctx op
                return Ok(updated, attempt)
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
    /// Chaining and allocation are delegated to the same private helper
    /// `applyAndPersist` uses, so there is exactly one place in the codebase
    /// that computes a record's `PreviousHash` / `Sequence` — a second
    /// implementation is how a stream silently mis-chains.
    let journalApplied<'Msg> (sink: IOpStreamSink<'Msg>) (ctx: PersistContext) (op: TreeOp<'Msg>) : Async<unit> =
        async {
            let! _attempt = persistAndReport sink ctx op
            return ()
        }

#if !FABLE_COMPILER
    /// The telemetry row for one apply, given the outcome the PERSIST settled
    /// on (Phase 1525, FGP 5). `sequence` is the address the record actually
    /// took when it was persisted, and the address it would have taken when it
    /// was not.
    let private telemetryFor<'Msg>
        (ctx: PersistContext)
        (op: TreeOp<'Msg>)
        (sequence: int)
        (outcome: OpOutcome)
        (elapsedMs: float)
        : OpApplyTelemetry =
        { StreamId = ctx.StreamId
          Sequence = sequence
          OpKind = OpKind.ofTreeOp op
          NodeId = OpApplyTelemetry.topLevelNodeId op
          Outcome = outcome
          TimeToApplyMs = elapsedMs
          PromptId = ctx.PromptId
          UserId = ctx.UserId
          Timestamp = currentTimestamp () }

    let private emitTelemetry (telemetrySink: IFuaranTelemetrySink) (telemetry: OpApplyTelemetry) : unit =
        try
            telemetrySink.RecordOpApply telemetry
        with _ ->
            // Telemetry is best-effort by contract; never let a sink throw
            // poison the apply + persist path.
            ()

    /// Apply `op` against `tree` ONCE and fan out to BOTH sinks: persist a
    /// hash-chained `OpRecord` to the op-stream `sink` (on `Ok`) AND emit one
    /// `OpApplyTelemetry` to `telemetrySink` (on every outcome, success or
    /// failure). The recommended call site for hosts that want durability
    /// AND telemetry (Phase 124, FGP 5).
    ///
    /// **The telemetry row is emitted AFTER the append settles, and names what
    /// the append did** (Phase 1525). Before this it was emitted BEFORE the
    /// append with `Outcome = Applied`, so an op the sink then lost left a row
    /// claiming success at a `(StreamId, Sequence)` naming no record — the join
    /// key pointed at nothing and no reader could tell. A lost op now emits
    /// `OpOutcome.PersistLost` carrying the reason.
    ///
    /// The op is applied exactly once (no double-apply): both records are
    /// derived from the single `Apply.apply` result. On an APPLY failure no
    /// record is persisted and the telemetry carries the would-be next sequence,
    /// exactly as before. Both sinks stay best-effort: a telemetry throw is
    /// swallowed, and a persist failure is reported through `ctx.OnSinkError`
    /// (or the default report) without breaking the apply path.
    let applyWithSinks<'Msg>
        (sink: IOpStreamSink<'Msg>)
        (telemetrySink: IFuaranTelemetrySink)
        (ctx: PersistContext)
        (op: TreeOp<'Msg>)
        (tree: Node<'Msg>)
        : Async<Result<Node<'Msg>, ApplyError>> =
        async {
            let sw = Stopwatch.StartNew()
            let result = Apply.apply op tree
            sw.Stop()
            let elapsedMs = sw.Elapsed.TotalMilliseconds

            match result with
            | Error e ->
                // Nothing is persisted, so the address is the one the record
                // WOULD have taken — the pre-1525 behaviour, unchanged.
                let! latest = sink.LatestSequence ctx.StreamId

                emitTelemetry
                    telemetrySink
                    (telemetryFor ctx op (latest + 1) (OpOutcome.ofApplyResult result) elapsedMs)

                return Error e
            | Ok updated ->
                let! attempt = persistAndReport sink ctx op

                let sequence, outcome =
                    match attempt with
                    | PersistAttempt.Persisted sequence -> sequence, OpOutcome.Applied
                    | PersistAttempt.Failed(sequence, failure) ->
                        sequence, OpOutcome.PersistLost(PersistFailure.describe ctx.StreamId sequence failure)

                emitTelemetry telemetrySink (telemetryFor ctx op sequence outcome elapsedMs)
                return Ok updated
        }

    /// `applyWithSinks` under an INVOCATION KEY — the retry-safe call site
    /// (Phase 1485).
    ///
    /// `applyWithSinks` above swallows a telemetry throw AFTER the op-stream
    /// append has committed, so a caller that reads the throw as "the call
    /// failed" and retries appends a second record: the unkeyed wrapper cannot
    /// tell a retry from a fresh op, because nothing in an op says which
    /// invocation produced it. This entry point takes that missing fact from
    /// the caller and hands it to the sink, so the SECOND call for a key
    /// persists nothing and answers with the first call's receipt.
    ///
    /// The sink is typed `IOpStreamKeyedSink<'Msg>` rather than probed for at
    /// run time. A host cannot then ask for idempotency from a store that has
    /// no key index and receive a silent plain append — the one outcome worse
    /// than not offering the contract at all. Both sinks shipped in this tier
    /// implement it.
    ///
    /// `invocationKey` is opaque and scoped to `ctx.StreamId`. It names ONE
    /// intent of the caller's — a command id, a request id, an idempotency
    /// header — and two genuinely distinct user actions that share a key are
    /// collapsed into one record, so a key derived from the op alone is wrong
    /// wherever the same op may legitimately be applied twice.
    ///
    /// Returns the applied tree exactly as `applyWithSinks` does, including on
    /// a duplicate: the apply is deterministic and ran against the same tree,
    /// so the caller's state is correct either way — what the key changes is
    /// what is DURABLE, not what is returned. And as in `applyWithSinks`, the
    /// telemetry row is emitted AFTER the append settles and names its outcome
    /// (Phase 1525) — a DUPLICATE counts as persisted, because the record the
    /// key names is in the stream and the join key resolves.
    let applyWithSinksKeyed<'Msg>
        (sink: IOpStreamKeyedSink<'Msg>)
        (telemetrySink: IFuaranTelemetrySink)
        (ctx: PersistContext)
        (invocationKey: string)
        (op: TreeOp<'Msg>)
        (tree: Node<'Msg>)
        : Async<Result<Node<'Msg>, ApplyError>> =
        async {
            let baseSink = sink :> IOpStreamSink<'Msg>

            let sw = Stopwatch.StartNew()
            let result = Apply.apply op tree
            sw.Stop()
            let elapsedMs = sw.Elapsed.TotalMilliseconds

            match result with
            | Error e ->
                let! latest = baseSink.LatestSequence ctx.StreamId

                emitTelemetry
                    telemetrySink
                    (telemetryFor ctx op (latest + 1) (OpOutcome.ofApplyResult result) elapsedMs)

                return Error e
            | Ok updated ->
                let! latest = baseSink.LatestSequence ctx.StreamId
                let sequence = latest + 1
                let! built = buildRecordAt baseSink ctx sequence op

                let! attempt =
                    async {
                        match built with
                        | Error reason -> return PersistAttempt.Failed(sequence, PersistFailure.SinkRefused reason)
                        | Ok record ->
                            try
                                let! keyed = sink.AppendKeyed(record, invocationKey)

                                match keyed with
                                | KeyedAppendOutcome.Appended receipt ->
                                    return PersistAttempt.Persisted receipt.Sequence
                                | KeyedAppendOutcome.Duplicate receipt ->
                                    // The record this key names is already in the
                                    // stream, so the join key resolves and the op
                                    // IS durable. That is a persisted outcome, not
                                    // a lost one.
                                    return PersistAttempt.Persisted receipt.Sequence
                            with ex ->
                                return PersistAttempt.Failed(sequence, PersistFailure.SinkRefused ex.Message)
                    }

                match attempt with
                | PersistAttempt.Failed(failedAt, failure) ->
                    reportFailure ctx failedAt failure

                    emitTelemetry
                        telemetrySink
                        (telemetryFor
                            ctx
                            op
                            failedAt
                            (OpOutcome.PersistLost(PersistFailure.describe ctx.StreamId failedAt failure))
                            elapsedMs)
                | PersistAttempt.Persisted persistedAt ->
                    emitTelemetry telemetrySink (telemetryFor ctx op persistedAt OpOutcome.Applied elapsedMs)

                return Ok updated
        }
#endif
