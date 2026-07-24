# Phase 171 – Provider-call telemetry channel (`RecordProviderCall`)

Adds a fourth telemetry channel to [`Fuaran.UI.Telemetry.Abstractions`](../../src/Fuaran.UI.Telemetry.Abstractions/): an additive `RecordProviderCall` member on `IFuaranTelemetrySink` carrying a typed `ProviderCallTelemetry` record. Provider-call outcomes (success, timeout, 429, auth failure, malformed-200, cancellation) become a first-class telemetry fact on their own stream – distinct from op-apply, render-failure, and policy-denial records.

Resolves the open `CONFLICTS-AND-GAPS.md` tension *"Provider-failure telemetry overloads the deny channel"* (surfaced by Phase 123): before this channel existed, provider transport failures were shoehorned onto the `DenyTelemetry` channel with a `ToolName = "provider:<name>"` prefix, polluting the drift detector's denial-rate signal with non-policy noise.

## What changes

Wholly additive (one new abstract member + one new record family + a no-op impl on every in-repo sink + a new optional drift signal):

| Surface | Status |
|---|---|
| `Fuaran.UI.Telemetry.Abstractions.ProviderOperation` DU | new – `Emit` / `Other of label` (the host carries any non-emit operation as an opaque label) (+ `ProviderOperation.name`) |
| `Fuaran.UI.Telemetry.Abstractions.ProviderCallOutcome` DU | new – closed, host-neutral outcome taxonomy (+ `name` / `isSuccess` / `isFailure`) |
| `Fuaran.UI.Telemetry.Abstractions.ProviderTokenUsage` record | new – `{ InputTokens; OutputTokens }` |
| `Fuaran.UI.Telemetry.Abstractions.ProviderCallTelemetry` record | new – the per-call telemetry record |
| `IFuaranTelemetrySink.RecordProviderCall` | new abstract member |
| `Fuaran.UI.Telemetry.Default` sinks | `RecordProviderCall` implemented on `NoOpSink` / `InMemorySink` (+ `ProviderCallRecords` reader + `Clear`) / `ConsoleSink` / `ConsoleDevToolsSink` (+ `ShowProviderCall` option) |
| `Fuaran.UI.Telemetry.Drift.ProviderDrift` | new – provider-reliability (success-rate) drift detector, the success-rate twin of `Detect` over the provider-call stream |

## `ProviderCallTelemetry` shape

```fsharp
type ProviderOperation =
    | Emit
    | Other of label: string

type ProviderCallOutcome =
    | Success
    | Transport
    | ProviderError of status: int
    | Malformed
    | Cancelled
    | EmptyCompletion
    | NotConfigured
    | MissingKey
    | Other of detail: string

type ProviderTokenUsage = { InputTokens: int; OutputTokens: int }

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
```

`ProviderCallOutcome` is **closed and structural** – host sinks encode it without a host-error-type-aware codec, exactly like `OpOutcome`. The cases are the Phase 123 provider error-classification taxonomy (transport / `provider-error:{status}` / malformed / cancelled / empty-completion / not-configured / missing-key) lifted into the language tier, plus `Success` and an `Other` escape hatch. Hosts map their own provider-error type onto these cases at the call site; the DU carries no host dependency.

`ProviderCallOutcome.name` emits the same stable tokens the Phase 123 `ChatError.TelemetryClass` used (`success` is the only addition), so existing op-stream filters keep matching. Correlation ids follow the Phase 12.T attribution precedent: `UserId` is required (always known at a provider call site); `SessionId` / `PromptId` are `option`.

## Interface-evolution mechanism (the decision)

`IFuaranTelemetrySink` gains an abstract member. Per the **established precedent** the interface itself documents (`RecordRenderFailure` doc comment; the `IFuaranRuntime.CanDispatch` note in `STABILITY.md`):

> F# interfaces cannot carry a true default implementation, so a new member is technically a recompile for direct implementers; adding it is a **pre-1.0 minor add**, and all in-repo implementers are updated in the same change.

This is the mechanism chosen here – **a plain abstract member, pre-1.0 minor add, not a major-version bump and not a default interface member.** Rationale: it matches the two prior sink/runtime extensions byte-for-byte; a `default`-interface-member alternative is unavailable in F# per the repo's stated position; and a major bump would be disproportionate for a pre-1.0 additive seam.

### What an implementor must add

Every direct `IFuaranTelemetrySink` implementor adds one member alongside its existing three:

```fsharp
member _.RecordProviderCall (telemetry: ProviderCallTelemetry) = ()   // no-op: explicit, not accidental
```

In-repo implementors updated in this change-set:

- `Fuaran.UI.Telemetry.Default` – `NoOpSink`, `InMemorySink`, `ConsoleSink`, `ConsoleDevToolsSink`.
- Test sinks in `Fuaran.UI.Telemetry.Tests`, `Fuaran.UI.OpStream.Tests`, `Fuaran.UI.Tests`.
- The orchestration-engine fake sinks in the engine test suite.

External implementors (any host implementing `IFuaranTelemetrySink` against its own backend, plus the consumer demo's in-repo test sink + its `ProviderDiagnostics.recordFailure` helper) add the same one-line member when they consume the bumped abstractions package. Until they do, they fail to compile against the new package version – the correct, visible minor-bump signal.

## Engine emission (orchestration tier)

The orchestration engine emits one `ProviderCallTelemetry` at its provider call site (the authoring-loop emit turn), **success AND failure both** – success volume is the denominator any failure-rate signal needs. The call site:

1. Stopwatch around the provider call.
2. On success → `Outcome = Success`, `TokenUsage` mapped from the provider response's usage (when reported), `Operation = Emit`.
3. On failure → the provider error is classified onto `ProviderCallOutcome` via the engine-local `ProviderTelemetry.classify` mapper (forge's `AIProviderError` cases → the closed outcome DU), `Operation = Emit`.
4. Correlation ids: `UserId` + `SessionId` from the user-scoped entity id, `PromptId` from the turn id.

Nothing provider-shaped routes to the deny channel any more. Two new opt-in wiring entry points carry the sink without breaking existing callers:

- `Engine.startShardRegionWithTelemetry … (telemetry: IFuaranTelemetrySink)` – `startShardRegion` (unchanged signature) delegates to it with an engine-local no-op sink.
- `Host.startEngineWithTelemetry … (telemetry: IFuaranTelemetrySink)` – `startEngine` (unchanged signature) delegates likewise.

A host that wants provider telemetry switches to the `…WithTelemetry` entry point and passes its sink; a host that does nothing keeps the no-op default and sees no behavioural change.

## Drift-detector separation

`Fuaran.UI.Telemetry.Drift.ProviderDrift.run` is the provider-reliability signal – the success-rate twin of `Detect.run`, keyed by provider id over the `ProviderCallTelemetry` stream, using the **same** `DriftThresholds` mechanics (default 10pp drop, `MinBaselineSamples` noise floor).

The separation is pinned at the **type level**: `ProviderDrift.run` takes `ProviderCallTelemetry seq`; the denial-rate signal is computed over `DenyTelemetry`; the op-apply signal over `OpApplyTelemetry`. The three record types are disjoint, so a provider failure can never inflate the denial-rate signal. The acceptance test (`Fuaran.UI.Telemetry.Tests/SinkTests.fs`, *"a provider-call record never lands on the deny channel"*) confirms a provider outcome produces no `DenyTelemetry` record.

## Stability impact

Per [`STABILITY.md`](../../STABILITY.md), `Fuaran.UI.Telemetry.Abstractions` is pre-1.0. The change is additive in the spirit of the post-1.0 minor rules: a new abstract member (recompile-for-implementers, the documented pre-1.0 sink-evolution shape) + new record types + a new concrete drift surface. No existing record, member, or wire shape changes. Consumers pin the bumped abstractions version on their next iteration and add the one-line member.

## Verification steps

1. `dotnet build` – clean (`TreatWarningsAsErrors`).
2. `dotnet fantomas .` – clean.
3. `dotnet run --project src/Fuaran.UI.Telemetry.Tests` – sink round-trips + channel-separation + provider-drift tests pass.
4. `dotnet run --project src/Fuaran.UI.OpStream.Tests` / `src/Fuaran.UI.Tests` – the updated in-repo sinks compile + pass with no regressions.
5. Engine side: `dotnet build` the orchestration solution after `pack-all.ps1`; the engine provider-telemetry tests assert a success call and a failure call each emit one `ProviderCallTelemetry` (correct outcome + correlation ids) and zero `DenyTelemetry`.

## See also

- [`12-T-telemetry.md`](12-T-telemetry.md) – the op-apply + deny telemetry seam this extends; the `(StreamId, Sequence)` correlation + attribution precedent.
- `roadmap/CONFLICTS-AND-GAPS.md` – the *"Provider-failure telemetry overloads the deny channel"* tension struck by this phase.
- `roadmap/phases/171-provider-call-telemetry-channel.md` – the phase body.
