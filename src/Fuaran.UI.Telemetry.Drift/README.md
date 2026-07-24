# Fuaran.UI.Telemetry.Drift

Drift detector for [`Fuaran.UI.Telemetry.Abstractions`](https://www.nuget.org/packages/Fuaran.UI.Telemetry.Abstractions) `OpApplyTelemetry` records.

Computes per-`OpKind` aggregate metrics (success rate, p50/p95 latency, top decoder-rejection reasons, top NodeNotFound NodeIds) and flags week-on-week success-rate regressions whose drop exceeds an operator-tunable threshold (default 10pp).

Two op-apply surfaces:

- **`Fuaran.UI.Telemetry.Drift.Aggregates.compute`** — group records by `OpKind`, compute the per-kind aggregate.
- **`Fuaran.UI.Telemetry.Drift.Detect.run`** — given a baseline window and a current window, emit one `DriftFinding` per `OpKind` (`Regression` / `NoRegression` / `InsufficientData`).

Defaults live in [`Fuaran.UI.Telemetry.Drift.Defaults`](Defaults.fs); operators override per-deployment by passing a configured `DriftThresholds` record to `Detect.run`.

## Style drift (`StyleDrift`, Phase 151)

The resolved-style twin of the op-apply detector: it catches *visual* regressions the way `Detect.run` catches *behavioural* ones. [`StyleDrift.detect`](StyleDrift.fs) takes a baseline and a current window of `StyleObservation` (the values [`Fuaran.UI.StyleObserver`](https://www.nuget.org/packages/Fuaran.UI.StyleObserver.Abstractions)'s `IStyleObserver.Subscribe` already emits) and returns a `StyleDriftReport`:

- **`Introduced`** — `(NodeId, flag-kind)` violations present in the current window and absent in the baseline (a render that *added* a `ContrastBelowAA` / `UsageBudgetExceeded` / … flag). This is the regression set.
- **`Cleared`** — violations present in the baseline and gone in the current window (a fix).
- **`WeightedSeverity`** — the introduced violations summed by the violated invariant's `Invariant.Weight` (Phase 145); manifest-free flags weight at the default `1.0`.
- **`StyleDrift.regressionDetected report`** — a CI-gate predicate (`true` iff at least one violation was introduced).
- **`StyleDrift.formatReport report`** — a one-line summary in the manifest's own vocabulary (`introduced 2 style violation(s) across 2 node(s) (… worst: UsageBudgetExceeded brand (budget 10.0%, observed 22.0%) on n1)`).

This is the Chromatic / Percy / Applitools visual-regression idea made **semantic and deterministic**: the diff is a set comparison keyed on `(NodeId, StyleFlag.kind)`, not screenshot pixels — reproducible, cheap, CI-gateable, and reported in declared-invariant terms rather than "these pixels changed". It does **not** introduce a new sink: the observations are the ones the observer already emits (FGP 5 — reuse the existing emission path).

Console entry point — `dotnet run --project src/Fuaran.UI.Telemetry.Drift` — prints a wiring guide and a synthetic 100-record demonstration. Cron-friendly scheduling lives in the host's deployment toolchain (each host wires it against its own observability backend).

Apache-2.0 licensed — see the repo [LICENSE](../../LICENSE).
