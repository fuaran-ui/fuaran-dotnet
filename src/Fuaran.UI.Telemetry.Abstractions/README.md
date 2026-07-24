# Fuaran.UI.Telemetry.Abstractions

Type contract for Fuaran's op-apply + authorizing-runtime-deny telemetry — the observability counterpart to the [`Fuaran.UI.Ops`](https://www.nuget.org/packages/Fuaran.UI.Ops) apply engine and the downstream orchestration tier's authorizing runtime (shipped as a separate package family).

Every applied op surfaces as an `OpApplyTelemetry` record. Every denied tool call surfaces as a `DenyTelemetry` record. Hosts implement `IFuaranTelemetrySink` against their own observability backend. Default sinks (NoOp / InMemory / Console) live in [`Fuaran.UI.Telemetry.Default`](https://www.nuget.org/packages/Fuaran.UI.Telemetry.Default).

See the [Phase 12.T migration doc](../../docs/migrations/12-T-telemetry.md) for the wiring at the AuthorizingRuntime deny branch and the apply-engine dispatch point, the drift-detector aggregate metrics, and the `PromptId` correlation pattern.

`(StreamId, Sequence)` on `OpApplyTelemetry` matches the corresponding fields on `Fuaran.UI.OpStream.OpRecord` — hosts that wire both sinks can join telemetry to the durable op record by this composite key.

Apache-2.0 licensed — see the repo [LICENSE](../../LICENSE).
