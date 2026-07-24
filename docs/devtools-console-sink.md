# DevTools-console telemetry sink (`ConsoleDevToolsSink`)

_Phase 91 – the "passive narration" half of the live-debug console._

`ConsoleDevToolsSink` is an `IFuaranTelemetrySink` that pretty-prints the telemetry beacons Fuaran already emits – op-apply outcomes, authorizer denials, render failures – as severity-tagged, grouped DevTools-console records as they fire. It is the **push** counterpart to Phase 90's `window.__fuaran` REPL (which is **pull** – you ask): together they make the typed layer's behaviour legible in the browser console with no panel or extension.

It introduces **no new telemetry contract** – it renders only the existing `OpApplyTelemetry` / `DenyTelemetry` / `RenderFailureTelemetry` records that already ship through `IFuaranTelemetrySink`, and never gates dispatch (FGP 5: a pure read-projection of the canonical stream).

## Wiring

```fsharp
open Fuaran.UI.Telemetry.Default

// Dev default: narrate everything, at every severity, to stdout.
let sink = ConsoleDevToolsSink.create ()

// Quiet diagnostic build: only denials + render failures.
let quiet = ConsoleDevToolsSink.createWith ConsoleDevToolsOptions.denialsAndFailuresOnly

// Custom filter knobs.
let tuned =
    ConsoleDevToolsSink.createWith
        { ConsoleDevToolsOptions.defaults with
            MinSeverity = DevToolsLevel.Warn }
```

Register `sink` wherever the host installs its telemetry sink (the same seam `ConsoleSink` / `InMemorySink` use). Pair it with the Phase 90 REPL for a full in-page debug experience, and surface both in the `fuaran-live` Console tab (Phase 94).

## Severity model

| Record | Level |
| --- | --- |
| `OpApplyTelemetry` – `Applied` | `Info` |
| `OpApplyTelemetry` – any failure outcome | `Warn` |
| `DenyTelemetry` | `Warn` |
| `RenderFailureTelemetry` | `Error` |

`MinSeverity` filters by this rank (`Info < Warn < Error`); the per-record-type `Show*` toggles filter by kind. A record is rendered only when its toggle is on **and** its level passes the floor.

## Grouped browser rendering (the writer seam)

Output is written through an injectable `IDevToolsConsoleWriter` – one `Group(level, header, rows)` call per record. The default `ConsoleDevToolsWriter` renders to `System.Console` as a tagged header line plus indented `key: value` rows (and, under Fable's `System.Console` shim, lands at `console.log`).

A browser host that wants true collapsible `console.group` / `console.table` records injects its own writer and keeps the sink's record-to-format logic unchanged:

```fsharp
let sink =
    ConsoleDevToolsSink.createWithWriter (ConsoleDevToolsOptions.defaults, myBrowserGroupWriter)
```

This keeps `Fuaran.UI.Telemetry.Default` free of any Fable/browser dependency while leaving the grouped browser rendering pluggable – the intended injector is the `fuaran-live` Console tab (Phase 94). The sink writes **only** through this seam (no raw `Console.*` from the sink itself – FGP 4); the writer is best-effort, and a throwing writer never poisons the apply/dispatch path it observes.

## Stability

Additive – a fourth sink alongside `NoOp` / `InMemory` / `Console`; no change to the `IFuaranTelemetrySink` contract. The `window.__fuaran` global and the grouped-console output format are debug-only surfaces and are not covered by the language tier's semver guarantees.

## TypeScript mirror

A parity sink on the TS side is **deferred** – it is gated on a `@fuaran-ui/*` telemetry surface, which does not yet exist (the Wave 10 follow-up shipped op-stream / layout-observer / ai-tools, not a telemetry package). When a TS telemetry surface lands, the mirror ships with the identical record-to-format mapping so the console output is host-agnostic.
