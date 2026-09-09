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
//  `Subject` (Phase 1637) is a SECOND principal beside `UserId`, and the two do
//  not replace one another. `UserId` is the tenant-side principal the engine
//  entity is scoped to — who the work is for. `Subject` is the identity the call
//  was actually MADE UNDER once the host resolved access: whose key paid for it,
//  whose quota it drew on. They coincide in the ordinary case and diverge in
//  exactly the cases a reader of their own telemetry needs to see — a
//  service-account fallback, a delegated identity, a misconfigured resolver
//  attributing every call to one subject. Without it a tenant cannot verify the
//  per-identity attribution a runtime promises them, and a wrong resolution is
//  invisible until a bill is wrong.
//
//  It is OPTIONAL and defaults to `None`, so every existing construction and
//  every existing sink is unchanged, and a host that resolves no identity says
//  so by absence rather than by a placeholder. Its id is opaque to this tier and
//  its kind is drawn from the host's own vocabulary — the public tier coins no
//  identity vocabulary, exactly as it coins no operation vocabulary above.
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

/// The resolved identity a provider call was made under — an opaque `Id` plus a
/// host-owned `Kind`, and **never a credential**.
///
/// `Id` is opaque to this tier: it is compared and reported, never parsed. `Kind`
/// says what sort of identity it is (a user, a service account, a tenant, …) in
/// whatever vocabulary the host already uses for the distinction; the public tier
/// records the tag and does not interpret it, the same posture
/// `ProviderOperation.Other` takes for a call-site label.
///
/// **Construct through `TelemetrySubject.create`.** It is the supported
/// constructor and it refuses key material, so a host that resolves an identity
/// badly gets no record rather than a record carrying a secret. A record literal
/// compiles — the type is public because sinks read it — and it is the caller's
/// own guarantee at that point; this project's redaction posture for a durable
/// record is that the value never enters it, and `create` is where that is
/// enforced for this one.
type TelemetrySubject = { Id: string; Kind: string }

[<RequireQualifiedAccess>]
module TelemetrySubject =

    /// Credential shapes this tier refuses to carry, lower-cased and matched as
    /// PREFIXES of the trimmed id: the two `Authorization`-header spellings (a
    /// header value is not an identity), the widely-used secret-key prefixes,
    /// the issued-token prefixes of common code-hosting and chat platforms,
    /// cloud access-key ids, and the opening line of a PEM block of any kind.
    ///
    /// Deliberately prefix shapes and NOT an entropy or length score. A
    /// legitimate subject id is routinely a long opaque string — a GUID, a
    /// directory object id, a hashed pseudonym — so a length or entropy
    /// heuristic refuses real identities while catching no credential a prefix
    /// does not already catch. A guard that fires on correct input is one hosts
    /// route around, and a routed-around guard protects nothing.
    let private secretPrefixes =
        [ "bearer "
          "basic "
          "sk-"
          "sk_"
          "ghp_"
          "gho_"
          "ghu_"
          "ghs_"
          "ghr_"
          "github_pat_"
          "xoxb-"
          "xoxp-"
          "xoxa-"
          "xoxs-"
          "xoxr-"
          "xapp-"
          "akia"
          "asia"
          "aiza"
          "-----begin" ]

    /// True when `id` has the shape of key material rather than of an identity.
    /// Public so a host decoding a subject off its own wire can apply the same
    /// rule at its own boundary, where `create` is not on the path.
    ///
    /// What it does NOT claim: it is a shape test, not a secret detector. An
    /// opaque credential with no recognisable prefix passes it, and that is the
    /// honest limit — the guard narrows an obvious and repeatedly-observed
    /// mistake (pasting the credential where the identity goes) and never
    /// licenses a host to stop caring what it puts here.
    let looksLikeKeyMaterial (id: string) : bool =
        if String.IsNullOrWhiteSpace id then
            false
        else
            let normalised = id.Trim().ToLowerInvariant()

            let jwtShaped =
                // A compact JWS/JWT: a base64url-encoded JSON header (which always
                // begins `eyJ`) followed by two more dot-separated segments.
                normalised.StartsWith "eyj"
                && (normalised |> Seq.sumBy (fun c -> if c = '.' then 1 else 0)) >= 2

            jwtShaped
            || secretPrefixes
               |> List.exists (fun (prefix: string) -> normalised.StartsWith prefix)

    /// The supported constructor. `Error` carries a stable, key-free
    /// classification token — `blank-kind` / `blank-id` / `key-material` — on the
    /// same convention `ProviderCallOutcome.name` uses above, so a host can log
    /// WHY a subject was refused without logging the value that was refused.
    ///
    /// A blank id or kind is refused as well as key material: a record asserting
    /// that a call was made under the empty identity carries no attribution at
    /// all, and absence already has a spelling (`Subject = None`).
    let create (kind: string) (id: string) : Result<TelemetrySubject, string> =
        if String.IsNullOrWhiteSpace kind then Error "blank-kind"
        elif String.IsNullOrWhiteSpace id then Error "blank-id"
        elif looksLikeKeyMaterial id then Error "key-material"
        else Ok { Id = id; Kind = kind }

/// One outbound provider call's worth of telemetry, surfaced to the configured
/// `IFuaranTelemetrySink` from the orchestration engine's provider call site.
type ProviderCallTelemetry =
    {
        ProviderId: string
        ModelId: string
        Operation: ProviderOperation
        Outcome: ProviderCallOutcome
        LatencyMs: float
        TokenUsage: ProviderTokenUsage option
        SessionId: string option
        PromptId: string option
        UserId: string
        /// The resolved identity this call was made under, when the host resolved
        /// one — beside `UserId`, never instead of it (see the header note). It
        /// carries NO key material: construct it through `TelemetrySubject.create`,
        /// which refuses a credential-shaped id.
        ///
        /// **Absent-at-`None` on the wire.** This tier ships no encoder for this
        /// record — `TokenUsage` above sits under the same condition — so the
        /// obligation is on a host encoder, and it is the one every optional member
        /// in this format already carries: a `None` member is OMITTED, not written
        /// as null or as an empty subject. A record that predates the field, and one
        /// from a host that resolves no identity, therefore serialise identically to
        /// what they serialised before.
        Subject: TelemetrySubject option
        Timestamp: DateTimeOffset
    }
