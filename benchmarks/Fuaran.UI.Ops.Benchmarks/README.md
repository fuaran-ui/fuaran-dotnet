# Fuaran.UI.Ops.Benchmarks (Phase 200)

A BenchmarkDotNet harness over the apply engine and the
[Phase 183](../../docs/) incremental re-derivation + effect-aware memoisation
engine (`Fuaran.UI.Memo`). It captures a **committed baseline** — apply
throughput, per-op allocation, and memoisation hit-rate — so the Phase 183
"recompute only affected subtrees" win is provable and a silent regression to
full re-derivation is caught at the [Phase 201](../../../../roadmap/phases/201-performance-release-gate.md)
gate rather than in production.

**Package-excluded (FGP 2):** this project is `IsPackable=false` and is *not* in
Build.fs `packableProjects`, so no shipped `Fuaran.UI.*` surface gains the
BenchmarkDotNet dependency. It is not in the `Test` target either — running
benchmarks is an explicit, separate step.

## What it measures

Three apply paths, parameterised over three corpus sizes (`Small` ≈ a card,
`Medium` ≈ a metric panel, `Large` ≈ a dense dashboard region):

| Benchmark | Path | What it proves |
|---|---|---|
| `FullApply` (baseline) | bare `FragmentApply.apply`, no cache | the cold-derivation cost every apply pays without the engine |
| `MemoisedApplyHit` | `Engine.Apply` warm-cache structural HIT | the tree is reused, not re-derived |
| `IncrementalReapply` | `Engine.Reapply` single value edit | only one hole-address recomputes — the Phase 183 headline |

`[<MemoryDiagnoser>]` captures per-op allocation alongside wall-time. The
**memo hit-rate** over a representative edit session is measured separately
(`HitRate.fs`, off the hot path) via the Phase 183 `CacheStatTelemetry` channel.

## Op-stream write path (`OpStreamBenchmarks` + `AppendRate`)

The apply benchmarks above measure derivation only; the op-stream **write** path
— canonical encode → SHA-256 hash-chain → durable append — has its own harness so
the Phase 320 actor-in-hash change has an absolute cost number (is the larger
hashed pre-image cheap? is the generated codec cheap?). Parameterised over three
op shapes (`UpdateProp` ≈ a granular field edit, `InsertChild` ≈ a structural op
carrying a subtree, `Batch16` ≈ a wide multi-edit turn):

| Benchmark | Path | What it proves |
|---|---|---|
| `EncodeOp` (baseline) | `CanonicalJson.encodeOp` | the canonical-JSON encode cost alone |
| `ComputeHash` | `HashChain.computeHash` | encode + SHA-256 — the per-op hash-chain cost |
| `BuildRecord` | hash + materialise the `OpRecord` | the synchronous per-op record-production cost |

The **durable append** itself (`InMemorySink.Append`) is `Async` and rejects
duplicate `(StreamId, Sequence)` pairs, so it does not fit BenchmarkDotNet's
repeat-the-same-call hot loop. It is measured deterministically off the hot path
(`AppendRate.fs`, mirroring `HitRate.fs`): a bounded run of hash-chained appends
to a fresh InMemory sink, reporting mean wall-time + allocation per append.
InMemory is the floor — a Sqlite sink layers I/O on top, but the in-process
append (Dictionary insert + per-stream lock + async-state-machine cost) is the
bookkeeping core every sink shares.

## Build (this opener — done)

```powershell
dotnet build -c Release benchmarks/Fuaran.UI.Ops.Benchmarks/Fuaran.UI.Ops.Benchmarks.fsproj
```

The committed `apply-rederivation-baseline.json` ships in the **`pending`** state
(metric IDs + units declared, no numbers). Regenerate the template after a
catalogue change:

```powershell
dotnet run -c Release --project benchmarks/Fuaran.UI.Ops.Benchmarks -- emit-template
```

## Capture the baseline (DEFERRED — a benchmark run)

> Capturing numbers is a benchmark **run**: minutes of optimised execution on a
> fixed machine. It is out of scope for the build-only Wave T opener and must be
> done deliberately on a stable host.

```powershell
# Full suite — fills the mean_ns + alloc_b metrics:
dotnet run -c Release --project benchmarks/Fuaran.UI.Ops.Benchmarks -- --filter *

# Memo hit-rate over the edit session:
dotnet run -c Release --project benchmarks/Fuaran.UI.Ops.Benchmarks -- hit-rate

# Op-stream write path — encode + hash (BenchmarkDotNet), then durable append:
dotnet run -c Release --project benchmarks/Fuaran.UI.Ops.Benchmarks -- --filter *OpStream*
dotnet run -c Release --project benchmarks/Fuaran.UI.Ops.Benchmarks -- append-rate 20000

# Render spine — the Phase 207 per-node / per-frame allocators:
dotnet run -c Release --project benchmarks/Fuaran.UI.Ops.Benchmarks -- render-alloc 20000
```

The render-spine baseline ships its own artifact,
`render-allocation-baseline.json` (regenerate the pending template with
`-- emit-render-template`). Fill its `render.state_keys.*` /
`render.live_state_merge.*` / `render.class_vocabulary.*` metrics from the
`render-alloc` output, which prints them already keyed by metric id.

Those three families are the allocators [Phase 207](../../../../roadmap/phases/207-renderer-hot-path-allocation-reduction.md)
removed from the render path: the reactive subscription walk, the live-store
merge that precedes it, and the per-node class + ARIA-id vocabulary. Every edit
that phase made is OUTPUT-IDENTICAL, so the test suite cannot see a regression
in any of them — `alloc_b` is the number that can. (The SHAPE of those call
sites is locked separately, by `Fuaran.UI.Tests/HotPathVocabularyTests.fs`.)

The op-stream baseline ships its own artifact, `op-append-baseline.json`
(regenerate the pending template with `-- emit-op-template`). Fill its
`opstream.encode.*` / `opstream.hash.*` / `opstream.build_record.*` metrics from
the `*OpStream*` BenchmarkDotNet summary (`Mean` → `mean_ns`, `Allocated` →
`alloc_b`) and its `opstream.append.*` metrics from the `append-rate` output.

Then refresh `apply-rederivation-baseline.json`:

1. Set `status` → `captured`, stamp `captured_at_utc` (UTC ISO-8601) and
   `runtime` (`dotnet --version`, OS, CPU).
2. Fill each metric's `value` from the BenchmarkDotNet summary
   (`Mean` → `mean_ns`, `Allocated` → `alloc_b`) and the `hit-rate` output
   (`memo.hit_rate.edit_session`).
3. Commit the refreshed artifact. The [Phase 201](../../../../roadmap/phases/201-performance-release-gate.md)
   gate consumes it on every release.

The artifact shape is the cross-repo contract in
[`PERF_BASELINE_SCHEMA.md`](PERF_BASELINE_SCHEMA.md).
