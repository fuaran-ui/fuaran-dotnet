# Fuaran.UI.Telemetry.Default

Default `IFuaranTelemetrySink` implementations for the Fuaran op-apply + AuthorizingRuntime-deny telemetry contract declared in [`Fuaran.UI.Telemetry.Abstractions`](https://www.nuget.org/packages/Fuaran.UI.Telemetry.Abstractions).

Four sinks:

- **`NoOpSink`** — the explicit "I don't care" default at `Orchestration.installWithTelemetry`. Both records become unit-returning no-ops.
- **`InMemorySink`** — per-process ring buffer (default capacity 10 000 per record kind) for tests and ad-hoc inspection. The typed `OpApplyRecords` / `DenyRecords` properties expose the buffered records.
- **`ConsoleSink`** — `[fuaran.telemetry] op-apply ...` / `[fuaran.telemetry] deny ...` lines written to stdout. Useful for dev environments and small CLI tools; not for production hot paths.
- **`ConsoleDevToolsSink`** (Phase 91) — the "passive narration" half of the live-debug console: pretty-prints each beacon as a severity-tagged group (header + indented detail rows) instead of a flat line, with construction-time record-type + `MinSeverity` filter knobs. Output goes through an injectable `IDevToolsConsoleWriter` (the default `.NET` writer hits stdout; a browser host injects a `console.group`/`console.table` writer), so the grouped browser rendering is pluggable without this `.NET` package taking a Fable dependency. A pure read-projection of the telemetry stream — see [`docs/devtools-console-sink.md`](https://github.com/) for wiring.

Also ships `Fuaran.UI.Telemetry.Default.Apply.applyWithTelemetry` — a thin wrapper around `Fuaran.UI.Ops.Apply.apply` that emits one `OpApplyTelemetry` record per op (the opt-in seam until the Phase 12.Z `applyAndPersist` follow-up integrates both sinks at the apply-engine dispatch point).

Apache-2.0 licensed — see the repo [LICENSE](../../LICENSE).
