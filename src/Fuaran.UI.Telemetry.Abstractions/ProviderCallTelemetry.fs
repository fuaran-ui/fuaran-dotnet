namespace Fuaran.UI.Telemetry.Abstractions

open System

// ============================================================================
//  ProviderCallTelemetry — one structured record per outbound AI-provider call.
//
//  Emitted from the orchestration engine at every provider call site (the
//  authoring-loop emit turn), success AND failure both. A provider transport
//  error (timeout, 429, auth failure, malformed-200, cancellation) is a
//  first-class telemetry fact here — NOT an op-apply, NOT a render failure, and
//  NOT a policy denial. Before this channel existed (Phase 171) provider
//  failures were shoehorned onto the `DenyTelemetry` channel with a
//  `ToolName = "provider:<name>"` prefix, which polluted the drift detector's
//  denial-rate signal with non-policy noise (the open `CONFLICTS-AND-GAPS.md`
//  tension this record resolves). With provider outcomes on their own stream,
//  "the AI provider failed" and "policy denied the action" are distinguishable
//  facts, and the denial-rate drift signal reads clean.
//
//  `ProviderCallOutcome` is closed and structural — host sinks encode it
//  without a host-error-type-aware codec. The success case plus the Phase 123
//  provider error-classification taxonomy (transport / provider-error:{status}
//  / malformed / cancelled / empty-completion / not-configured / missing-key)
//  lifted into the language tier. Hosts map their own provider-error type
//  (a platform host's `AIProviderError`, the demo's `ChatError`, …) onto these cases at
//  the call site; the DU carries no host dependency. The `name` projection
//  emits the same stable tokens the Phase 123 `ChatError.TelemetryClass` used,
//  so existing op-stream filters keep matching.
//
//  Correlation ids follow the Phase 12.T attribution precedent: `UserId` is
//  always known at a provider call site (the engine entity is user-scoped), so
//  it is required; `SessionId` / `PromptId` are `option` for the same reason
//  the `OpApplyTelemetry` / `DenyTelemetry` ids are — operator-initiated and
//  non-session-bound calls legitimately have no value.
//
//  FGP 4 (diagnostics under both pipelines): the record shape is Fable-
//  compatible (no closures, no `System.Security.Cryptography`); `DateTimeOffset`
//  rounds through Fable cleanly per the `OpApplyTelemetry` / `RenderFailure`
//  precedent.
// ============================================================================

/// Which leg of the authoring loop a provider call served. `Emit` is the main
/// emission turn; every other call site is carried as an opaque, host-chosen
/// `label` the language tier records but does not interpret. The host owns the
/// vocabulary of non-emit operations — the public tier sees only opaque tags.
[<RequireQualifiedAccess>]
type ProviderOperation =
    /// The main authoring-emission turn — the model produces the UI emission.
    | Emit
    /// Any other provider call site; `label` is an opaque, stable host-chosen
    /// tag (e.g. an op-stream filter token) the language tier does not interpret.
    | Other of label: string

[<RequireQualifiedAccess>]
module ProviderOperation =
    /// Stable short name for op-stream filters + host aggregates / formatters.
    /// `Other` returns its `label` verbatim, so the host supplies the exact
    /// stable token (the public tier never coins an operation vocabulary).
    let name (operation: ProviderOperation) : string =
        match operation with
        | ProviderOperation.Emit -> "emit"
        | ProviderOperation.Other label -> label

/// Closed, host-neutral classification of a provider-call outcome — the
/// success case plus the Phase 123 provider error-classification taxonomy
/// lifted into the language tier. `Other` is the bounded escape hatch for a
/// host error case that does not map onto a named category (e.g. a platform
/// host's `UnsupportedCapability` / `SchemaUnsupported`).
[<RequireQualifiedAccess>]
type ProviderCallOutcome =
    /// The provider returned a usable completion.
    | Success
    /// A transport-level failure (DNS / TLS / socket / connection reset).
    | Transport
    /// The provider returned a non-success HTTP status. `status` keeps a
    /// rate-limit (429) distinguishable from other provider-side failures.
    | ProviderError of status: int
    /// The provider returned a success status with a body the decoder could
    /// not read (schema drift, content-policy refusal shape, invalid JSON).
    | Malformed
    /// The request was cancelled or timed out.
    | Cancelled
    /// The provider returned a well-formed success with no usable completion
    /// text (empty / whitespace content).
    | EmptyCompletion
    /// The requested provider is recognised but has no working configuration.
    | NotConfigured
    /// No credential / API key was supplied for the call.
    | MissingKey
    /// A host error case outside the named taxonomy. `detail` is a stable,
    /// key-free classification token (never carries a secret).
    | Other of detail: string

[<RequireQualifiedAccess>]
module ProviderCallOutcome =

    /// Stable, key-free classification token for op-stream filters + host
    /// aggregates. Mirrors the Phase 123 `ChatError.TelemetryClass` token set
    /// (`success` is the only addition) so existing filters keep matching.
    let name (outcome: ProviderCallOutcome) : string =
        match outcome with
        | ProviderCallOutcome.Success -> "success"
        | ProviderCallOutcome.Transport -> "transport"
        | ProviderCallOutcome.ProviderError status -> sprintf "provider-error:%d" status
        | ProviderCallOutcome.Malformed -> "malformed"
        | ProviderCallOutcome.Cancelled -> "cancelled"
        | ProviderCallOutcome.EmptyCompletion -> "empty-completion"
        | ProviderCallOutcome.NotConfigured -> "not-configured"
        | ProviderCallOutcome.MissingKey -> "missing-key"
        | ProviderCallOutcome.Other detail -> sprintf "other:%s" detail

    /// True only for the success case. The denominator any failure-rate signal
    /// needs is the full call volume; `isFailure` is its complement.
    let isSuccess (outcome: ProviderCallOutcome) : bool =
        match outcome with
        | ProviderCallOutcome.Success -> true
        | _ -> false

    /// True for every non-success outcome — the provider-failure-rate signal.
    let isFailure (outcome: ProviderCallOutcome) : bool = not (isSuccess outcome)

/// Optional token-usage counts a provider may report alongside a completion.
/// Both counts are best-effort: a provider that does not report usage yields
/// `ProviderCallTelemetry.TokenUsage = None` rather than zeroes.
type ProviderTokenUsage = { InputTokens: int; OutputTokens: int }

/// One outbound provider call's worth of telemetry, surfaced to the configured
/// `IFuaranTelemetrySink` from the orchestration engine's provider call site.
type ProviderCallTelemetry =
    { ProviderId: string
      ModelId: string
      Operation: ProviderOperation
      Outcome: ProviderCallOutcome
      LatencyMs: float
      TokenUsage: ProviderTokenUsage option
      SessionId: string option
      PromptId: string option
      UserId: string
      Timestamp: DateTimeOffset }
