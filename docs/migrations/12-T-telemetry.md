# Phase 12.T – Op-apply telemetry + AuthorizingRuntime denial audit

Ships [`Fuaran.UI.Telemetry.Abstractions`](../../src/Fuaran.UI.Telemetry.Abstractions/) + [`Fuaran.UI.Telemetry.Default`](../../src/Fuaran.UI.Telemetry.Default/) + [`Fuaran.UI.Telemetry.Drift`](../../src/Fuaran.UI.Telemetry.Drift/) – a structured-record telemetry seam over Fuaran's apply-engine + AuthorizingRuntime, with an aggregate-metrics + drift-detection helper. Subsumes gap-scan finding #8 (AuthorizingRuntime denials produce no audit trail).

§4f of the Fuaran UI language design frames the closed-loop orchestrator – the orchestrator tier needs per-op + per-deny aggregate signal to tune the policy and the AI's prompts. 12.T makes that observability seam real. Foundation for compliance posture: regulated-data host applications need denial logs as evidence the policy is enforced; per-op telemetry is evidence of what AI is doing in production.

## What changes

Three new packages, one additive field on a shared record, two new optional-sink install variants:

| Surface | Status |
|---|---|
| `Fuaran.UI.Telemetry.Abstractions` package | new – `OpKind`, `OpApplyTelemetry`, `OpOutcome`, `DenyTelemetry`, `IFuaranTelemetrySink` |
| `Fuaran.UI.Telemetry.Default` package | new – `NoOpSink`, `InMemorySink`, `ConsoleSink`, `Apply.applyWithTelemetry` |
| `Fuaran.UI.Telemetry.Drift` package | new – `Aggregates.compute`, `Detect.run`, console runner |
| `ClientToolContext.PromptId: string option` | additive field (defaults to `None`) |
| `ClientToolContext.UserId: string option` | additive field (defaults to `None`); follow-up – feeds `DenyTelemetry.UserId` via `defaultArg ctx.UserId ""` |
| `Orchestration.installWithTelemetry` + `installWithTelemetryInScope` | new – accept `IFuaranTelemetrySink`; existing `install` / `installInScope` delegate with the in-process no-op sink |
| `AuthorizingRuntime`'s third ctor arg `IFuaranTelemetrySink` | new – `Deny` branch emits one `DenyTelemetry` before returning the deny envelope |

Wholly additive. STABILITY-clean: pre-1.0 minor-version bump; no breaking changes to the orchestrator tier's public install entry point.

## OpApplyTelemetry shape

```fsharp
type OpOutcome =
    | Applied
    | DecoderRejected of reason: string
    | NodeNotFound of nodeId: string
    | ApplyEngineError of detail: string

type OpApplyTelemetry =
    { StreamId: string
      Sequence: int
      OpKind: OpKind
      NodeId: string option
      Outcome: OpOutcome
      TimeToApplyMs: float
      PromptId: string option
      UserId: string
      Timestamp: DateTimeOffset }
```

`(StreamId, Sequence)` matches the corresponding fields on `Fuaran.UI.OpStream.OpRecord` – hosts that wire both sinks can join telemetry to the durable op record by this composite key. `OpKind` is the case-name projection of `TreeOp<'Msg>` (via `OpKind.ofTreeOp`) so telemetry stays `'Msg`-erased.

`OpOutcome.DecoderRejected` is reserved for the storage-shape follow-on (the `Node<obj>` + `moduleMsgDecoder` pair §4g flags as out-of-scope for v1). The typed apply produces only `Applied` / `NodeNotFound` / `ApplyEngineError` in v1.

## DenyTelemetry shape

```fsharp
type DenyTelemetry =
    { ToolName: string
      Reason: string
      ActiveModule: string option
      ActivePage: string option
      PromptId: string option
      UserId: string
      Timestamp: DateTimeOffset }
```

All of `ActiveModule` / `ActivePage` / `PromptId` / `UserId` come from the `ClientToolContext` the AI runtime supplied at executor-invoke time (`UserId` via `defaultArg ctx.UserId ""` – see § "UserId scope-limit"). `Reason` is the policy gate's deny reason; `ToolName` is the tool's registered name.

### UserId scope-limit – resolved (follow-up landed)

The v1 12.T ship filled `DenyTelemetry.UserId` with the empty-string sentinel (`""`) at the `AuthorizingRuntime`'s deny-record construction site: the opener's task list sourced `PromptId` from `ClientToolContext` but did not prescribe a source for `UserId`, so v1 deny records carried no user attribution. (Note: `OpApplyTelemetry.UserId` was never sentinel-limited – the apply seam's `Apply.ApplyContext.UserId` is a caller-supplied `string` and has always threaded a real value.)

**The deferred follow-up has now landed (path 1 of the two below).** `ClientToolContext` gains a symmetric `UserId: string option` field alongside `PromptId`. The `AuthorizingRuntime` deny branch now constructs `UserId = defaultArg ctx.UserId ""`, so:

- Hosts that thread a user id get real attribution on every deny record. The forge adapter's `Adapters.fs` `forgeCtx -> fuaranCtx` translator sources it from `UserSession.getUserId ()` (`UserId = Some(UserSession.getUserId ())`), so the blessed integration carries attribution out of the box.
- Hosts that supply `None` fall back to the same `""` sentinel – no behavioural change for them, and the field addition is additive (every existing literal construction site flips to `UserId = None`).

The deny-telemetry test (`AuthorizingRuntimeDenyTelemetryTests`) now asserts a non-empty `UserId` round-trips from `ClientToolContext` through to the recorded `DenyTelemetry`; the apply-side round-trip is asserted in `Fuaran.UI.Telemetry.Tests/ApplyTests.fs` (`UserId from context`).

The alternative path remains available for hosts with multiple identity surfaces:

2. **Host-side enrichment wrapper.** A host can wrap their `IFuaranTelemetrySink` with a thin enrichment layer that fills `UserId` from the host's ambient request context before persisting. This decouples user-attribution from `ClientToolContext` and is the right pattern when the host has multiple identity surfaces (machine user, end user, on-behalf-of).

## AuthorizingRuntime deny dispatch – wiring

```
ClientToolExecutor invoked
  → ArgsJsonContract.validate            (Phase 12.J — contract gate)
      ├─ Error detail   → return contract-violation envelope
      └─ Ok ()          → IFuaranClientToolAuthorizer.Authorize     (Phase 12.Y)
                            ├─ Allow         → executor body runs
                            └─ Deny reason   → emit DenyTelemetry  (Phase 12.T — this phase)
                                              → return deny envelope
```

The sink call is *after* the policy decision, *before* the deny envelope is returned. FGP 3: the sink is a sink, not a policy – it never gates dispatch. The executor body still does not run for denied calls.

A throwing sink is swallowed (per IFuaranTelemetrySink contract – telemetry is best-effort, hosts that want strict telemetry wrap a synchronous flushing sink). The deny envelope still returns to the AI runtime regardless.

## Apply-engine integration – `applyWithTelemetry` wrapper

Phase 12.T does NOT modify `Fuaran.UI.Ops.Apply.apply`. Two coordination paths were called out in the opener:

> Coordinate by inserting both sinks (op-stream + telemetry) in a single batched edit when the integration lands, OR ship 12.T's sink first and let 12.Z's apply-integration fan-out add the second sink to the same dispatch line.

12.T ships under the second path. The opt-in apply-engine seam lives in `Fuaran.UI.Telemetry.Default.Apply.applyWithTelemetry`:

```fsharp
type ApplyContext =
    { StreamId: string
      Sequence: int
      UserId: string
      PromptId: string option }

let applyWithTelemetry
    (sink: IFuaranTelemetrySink)
    (ctx: ApplyContext)
    (op: TreeOp<'Msg>)
    (tree: Node<'Msg>)
    : Result<Node<'Msg>, ApplyError>
```

The wrapper:

1. Starts a `Stopwatch`.
2. Calls `Fuaran.UI.Ops.Apply.apply` to materialise `Result<Node<'Msg>, ApplyError>`.
3. Stops the stopwatch.
4. Derives an `OpOutcome` from the apply result:
   - `Result.Ok _` → `OpOutcome.Applied`
   - `ApplyErrorCode.NodeNotFound` / `ParentNotFound` → `OpOutcome.NodeNotFound (extracted-id)`
   - every other `ApplyErrorCode` → `OpOutcome.ApplyEngineError "<code>: <message>"`
5. Emits one `OpApplyTelemetry` to the sink.
6. Returns the unmodified apply result.

Sink exceptions are swallowed; apply result propagates unchanged.

When the Phase 12.Z follow-up integrates `applyAndPersist` into `Fuaran.UI.Ops.Apply.fs` at the dispatch point, the same edit can fan out to both sinks (op-stream `Append` + telemetry `RecordOpApply`) in a single coordinated call. The `applyWithTelemetry` wrapper retires at that point.

## Drift detector

`Fuaran.UI.Telemetry.Drift` ships two surfaces:

- **`Aggregates.compute`** – group `OpApplyTelemetry` records by `OpKind`, compute the per-kind aggregate (sample count, applied count, success rate, p50/p95 latency, top decoder-rejection reasons, top NodeNotFound NodeIds).
- **`Detect.run`** – given a baseline window + a current window + `DriftThresholds`, emit one `DriftFinding` per `OpKind` (`Regression` / `NoRegression` / `InsufficientData`).

```fsharp
type DriftThresholds =
    { SuccessRateRegressionPct: float        // default: 0.10 (10pp drop)
      MinBaselineSamples: int }              // default: 50 records

let findings = Detect.run Defaults.thresholds baseline current
```

`Defaults.thresholds` is the static-default record. Operators override per-deployment.

`MinBaselineSamples` guards against false positives on noisy small windows – fewer than 50 baseline records returns `InsufficientData` rather than a regression warning. Operators running test harnesses lower it; operators running high-traffic deployments raise it.

Console runner: `dotnet run --project src/Fuaran.UI.Telemetry.Drift` prints a wiring guide + a synthetic 100/100-record demonstration that flags a 15pp drop on `OpKind.UpdateProp`. The cron-friendly scheduled-job wiring lives in the host's deployment toolchain.

## Open-core posture

Per the v1 OpStream package set's posture (`Fuaran.UI.OpStream.Abstractions` shipped 2026-05-26 under Apache-2.0), all three new Phase 12.T packages adopt the same posture. The Phase 12.X open-core posture reversal documented in [`12-X-posture-reversal.md`](12-X-posture-reversal.md) applies – the Abstractions tier is an internal modularity seam, not a third-party-implementation open-core invitation, and a single blessed private host integration remains the supported integration path.

The phase opener's "Apache 2.0 alongside the other abstractions per Phase 12.X's open-core pattern" guidance is superseded by the post-12.X reversal. The `.fsproj` `<Description>` lines on all three packages declare "Apache-2.0 licensed." consistent with the OpStream package set.

## Stability impact

Per [`STABILITY.md`](../../STABILITY.md), Fuaran is pre-1.0 – every minor version may break. The Phase 12.T changes are nonetheless wholly additive in the spirit of the post-1.0 minor-version rules:

- `ClientToolContext` gains a new field. Existing literal construction sites need a `PromptId = None` addition (already applied to every site in this repo: `ClientToolContext.defaultScope`, the forge adapter, the two affected test files).
- `AuthorizingRuntime` gains a new ctor arg but preserves the pre-12.T two-arg ctor (delegates with no-op sink).
- `Orchestration.install` / `installInScope` signatures are unchanged. New `installWithTelemetry` / `installWithTelemetryInScope` are non-breaking additions.

External consumers integrating via the blessed host integration see no breakage. The host adapter already lands `PromptId = None` in the `fuaranCtx` it constructs from a host-side request context – proper prompt-id threading is a host-side adoption follow-up.

## Verification steps

After landing 12.T:

1. **`dotnet build Fuaran.sln -c Release`** – clean. `TreatWarningsAsErrors=true` catches drift.
2. **`dotnet fantomas .`** – clean. No formatting drift in the new files.
3. **`dotnet run --project src/Fuaran.UI.Telemetry.Tests -c Release`** – 19 acceptance tests pass (OpKind, sinks, applyWithTelemetry, drift).
4. **`dotnet run --project src/Fuaran.UI.Tests -c Release`** – `Phase 12.T – AuthorizingRuntime deny telemetry` testList passes (3 tests). Existing test count unchanged (no regressions from the `ClientToolContext` additive field).
5. **`dotnet run --project src/Fuaran.UI.Telemetry.Drift -c Release`** – synthetic demonstration emits `UpdateProp REGRESSION baseline=90.0% current=75.0% drop=15.0pp`, satisfying the "Drift detector console run on a synthetic stream surfaces the expected regression case" acceptance criterion.
6. **`dotnet run --project src/Fuaran.UI.Ops.Tests -c Release`** – no regressions in the apply-engine suite. `Fuaran.UI.Ops.Apply.apply` is unchanged by 12.T.

## Rollback

Wholly additive. Reverting the seven 12.T commits backs out cleanly:

- Drop `Fuaran.UI.Telemetry.{Abstractions,Default,Drift,Tests}` projects + their `Fuaran.sln` / `Build.fs` registration.
- Drop the `Fuaran.UI.Telemetry.Abstractions` reference on the orchestrator tier's client package.
- Revert `Orchestration.fs` to the pre-12.T `AuthorizingRuntime(inner, authorizer)` shape; drop the `installWithTelemetry` / `installWithTelemetryInScope` entries; drop the inline `noOpTelemetrySink`.
- Drop `ClientToolContext.PromptId`; revert the four literal construction sites (`defaultScope`, `Adapters.fs`, two test files).
- Drop `src/Fuaran.UI.Tests/AuthorizingRuntimeDenyTelemetryTests.fs` + its `<Compile>` entry.

Consumer impact at rollback: forge has not yet adopted 12.T (the forge adapter's `fuaranCtx` already lands `PromptId = None`, no behavioural dependency). No external consumer breakage from rollback.

## See also

- The Fuaran UI language design §4f (conversation-as-source-of-truth – the downstream AI consumer is the primary deny+op-apply telemetry consumer), §4g (the op vocabulary the OpKind discriminator mirrors).
- [`12-Z-op-stream.md`](12-Z-op-stream.md) – the durable-op-record sibling; the `(StreamId, Sequence)` correlation pattern; the coordinated apply-engine integration this phase deferred to.
- The downstream runtime tier's authorizing runtime is the deny-record source (shipped 2026-05-24 in `Fern@83cf0ba`); its diagnostics `warn` surface is what the drift detector uses for cron-mode regression alerts.
- [`12-X-posture-reversal.md`](12-X-posture-reversal.md) – the abstractions-tier licensing posture (Telemetry.Abstractions inherits the same internal-modularity-seam framing).
