namespace Fuaran.UI.Telemetry.Abstractions

// ============================================================================
//  IFuaranTelemetrySink — sync fire-and-forget sink for op-apply + deny records.
//
//  Sync because the interface sits on the hot path of the apply engine
//  AND every authorizer dispatch — making it Async would impose an
//  Async.RunSynchronously / context-switch cost on every op and every
//  tool call. Concrete sinks that need durability buffer + flush async
//  on their own (the InMemorySink buffers in memory; a host's
//  Prometheus / OTel / file-backed sink batches internally).
//
//  Concrete sinks live in companion packages:
//   - Fuaran.UI.Telemetry.Default — NoOpSink, InMemorySink, ConsoleSink.
//   - Host-specific sinks       — a host implements against its platform's
//                                 telemetry implementation; other hosts
//                                 implement against their own backend.
//
//  FGP 3 (default-deny by shape, not by discipline): the sink is a sink,
//  not a policy — it never gates dispatch. `RecordDeny` is called AFTER
//  the deny decision, never before. The executor body still does not run
//  for denied calls.
// ============================================================================

/// The sink contract. All methods are sync fire-and-forget — implementations
/// that need durability buffer + flush async internally.
type IFuaranTelemetrySink =
    /// Record one op-apply outcome. Called from the apply-engine dispatch
    /// point AFTER the Result<_, ApplyError> is materialised, success or
    /// failure. Must not throw — sinks that fail internally swallow the
    /// failure (telemetry is best-effort by contract; a host that wants
    /// strict telemetry wraps a synchronous flushing sink).
    abstract member RecordOpApply: telemetry: OpApplyTelemetry -> unit

    /// Record one AuthorizingRuntime denial. Called from the Deny branch
    /// BEFORE the deny envelope is returned to the AI runtime — the
    /// executor body has already been gated. Must not throw, same reason
    /// as `RecordOpApply`.
    abstract member RecordDeny: telemetry: DenyTelemetry -> unit

    /// Record one render-time failure. Called from
    /// the renderer when the per-node render guard catches a throw or a
    /// `NodeKind.ErrorBoundary` contains a failing subtree. Must not throw,
    /// same contract as the apply / deny sides. By established
    /// precedent — `IFuaranTelemetrySink` gaining a new abstract
    /// member is a pre-1.0 minor add. Direct implementers add the member
    /// alongside their existing `RecordOpApply` / `RecordDeny`.
    abstract member RecordRenderFailure: telemetry: RenderFailureTelemetry -> unit

    /// Record one outbound AI-provider call. Called from the orchestration
    /// engine at every provider call site AFTER the call returns — success
    /// AND failure both (success volume is the denominator any failure-rate
    /// signal needs). Failures are classified via the closed
    /// `ProviderCallOutcome` taxonomy; nothing provider-shaped routes to the
    /// deny channel any more, so the denial-rate drift signal reads clean
    /// (Phase 171). Must not throw, same best-effort contract as the apply /
    /// deny / render sides. Following the established precedent above, adding
    /// this abstract member is a pre-1.0 minor add — direct implementers add
    /// `member _.RecordProviderCall _ = ()` alongside their existing members.
    abstract member RecordProviderCall: telemetry: ProviderCallTelemetry -> unit

    /// Record one memoised fragment-apply outcome (Phase 183). Called from the
    /// incremental re-derivation engine at every `apply` / `reapply` — hit,
    /// miss, incremental, or effecting-bypass. Makes the memo cache's hit rate
    /// + eviction pressure observable. Must not throw, same best-effort
    /// contract as the apply / deny / render / provider sides. Following the
    /// established precedent above, adding this abstract member is a pre-1.0
    /// minor add — direct implementers add `member _.RecordCacheStat _ = ()`
    /// alongside their existing members.
    abstract member RecordCacheStat: telemetry: CacheStatTelemetry -> unit

    /// Record one RUNTIME validation outcome for a tree (Phase 330). Called by
    /// whatever tier validated an emitted tree, AFTER validation returns —
    /// clean, warnings, errors, or not-run. This is the fourth leg of the
    /// interaction-correlation spine: with the same opaque `PromptId` on the
    /// op-record, the apply telemetry, the render telemetry and this record,
    /// one interaction is reconstructable end to end.
    ///
    /// Note what this is NOT: `Fuaran.UI.Validator` is a build-time AST walker
    /// over source and cannot see a tree produced at runtime. This member is
    /// the seam for the runtime counterpart; the build-time validator is a
    /// different job and is unaffected. Must not throw, same best-effort
    /// contract as every member above. Following the established precedent,
    /// adding this abstract member is a pre-1.0 minor add — direct
    /// implementers add `member _.RecordValidateOutcome _ = ()` alongside
    /// their existing members.
    abstract member RecordValidateOutcome: telemetry: ValidateOutcomeTelemetry -> unit
