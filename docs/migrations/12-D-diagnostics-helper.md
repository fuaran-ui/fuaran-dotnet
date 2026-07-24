# Phase 12.D – Orchestration diagnostics helper (consumer migration)

Shipped as `feat(Phase 12.D): close orchestration silent-failure surfaces with Diagnostics helper + duplicate-register warns + IClientToolRuntime evolution guard`.

This migration is **operator-visible but code-invisible** for typical hosts: every diagnostic the host/orchestrator tier emits now goes through its `Diagnostics` helper module, which prefixes every message with `[Fuaran.Orchestration]` and writes to `System.Diagnostics.Trace` under .NET in addition to `console.*` under Fable. Hosts that grep their browser console or trace output for orchestration warnings need to update their filter.

## What changes

| Surface | Pre-12.D | Post-12.D |
|---|---|---|
| Warning emission | `Browser.Dom.console.warn "..."` (Fable-only; throws under .NET) | `Diagnostics.warn "..."` (dual-pipeline; `[Fuaran.Orchestration]` prefix; opt-in bridge to `IModuleHost.PublishWarning`) |
| Message text | Free-text – varies per call site | Same text, **prefixed** `[Fuaran.Orchestration] ` |
| `Orchestration.install` re-entry with different args | Silently re-wires the resolver / snapshot subscriber | First wiring stays in effect; second call emits a warn pointing at `__resetForTest` |
| `SnapshotRegistry.registerProjector` duplicate | Silently overwrites | Warn unless `registerProjectorReplacing` is called instead |
| `ControllableRegistry.registerField` / `registerButton` duplicate | Silently overwrites | Warn unless `registerFieldReplacing` / `registerButtonReplacing` is called |
| `IClientToolRuntime` evolution | A new member added to the interface bypasses `IFuaranClientToolAuthorizer.Authorize` silently | `IClientToolRuntimeWrapCoverageTests` fails at build until `AuthorizingRuntime` adds an explicit wrap |

## Log-filter migration

The only operator-visible change. If your host has a console-grep or trace-grep that catches orchestration warnings (toast, log aggregator, error-budget alert), update its filter from a free-text search to a prefix anchor:

```diff
-/FastPathResolver|SnapshotRegistry|AIField/
+/^\[Fuaran\.Orchestration\] /
```

The prefix is stable across the orchestration tier; every warn/info/error helper applies it. Free-text searches against the original messages still work – the prefix is *additive* – but the prefix is the recommended filter going forward because it cleanly separates orchestration-tier diagnostics from unrelated `console.warn` callers in the host's own code.

## API impact (additive – no breaking changes)

```fsharp
// New module (in the orchestrator tier's client package) — pre-existing callers
// continue to work; new code routes through here.
module Diagnostics =
    val warn  : message: string -> unit
    val info  : message: string -> unit
    val error : message: string -> unit
    val bridgeToHost : host: IModuleHost -> unit
    val __resetForTest : unit -> unit

// SnapshotRegistry + ControllableRegistry — new explicit-replacement variants:
SnapshotRegistry.registerProjector "ModuleId" projector            // unchanged (default: warn on duplicate)
SnapshotRegistry.registerProjectorReplacing "ModuleId" projector   // suppress warn — deliberate replacement

ControllableRegistry.registerField "ModuleId" "fieldName" decoder
ControllableRegistry.registerFieldReplacing "ModuleId" "fieldName" decoder

ControllableRegistry.registerButton "ModuleId" "buttonName" msg
ControllableRegistry.registerButtonReplacing "ModuleId" "buttonName" msg
```

Consumers integrating via a private host integration pick up the helper automatically through the next integration bump – `Diagnostics.bridgeToHost host` will be wired by default so operator-visible toast surfaces continue to mirror orchestration warnings.

Non-forge hosts can opt in explicitly:

```fsharp
Diagnostics.bridgeToHost (host :> IModuleHost)
```

The bridge guards against infinite recursion if `host.PublishWarning` itself triggers a `Diagnostics.warn` (a host whose toast-render logs a warning won't loop).

## What hosts do NOT need to do

- No call-site changes to existing `Orchestration.install` invocations.
- No re-registration of fields / buttons / projectors. Same call shapes; the new optional arg is opt-in.
- No new dependency surface. `Diagnostics` lives in the orchestrator tier's client package – the package you're already consuming.

## Rollback

`Diagnostics` is additive and the duplicate-register warns are guarded by `~replace`. If a host needs to roll back the diagnostic prefix (e.g. a brittle log parser breaks on the `[Fuaran.Orchestration]` token), the warning text can be intercepted via `bridgeToHost` and rewritten before delivery to whatever downstream surface the host uses. The underlying `console.*` / `Trace.WriteLine` calls cannot be opted out of – they are the primary diagnostic channel.
