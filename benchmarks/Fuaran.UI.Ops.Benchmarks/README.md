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

## Capture the baselines (Phase 1613 — automated, no longer a hand transcription)

> Capturing numbers is a benchmark **run**: minutes of optimised execution. Do it
> deliberately on a stable host, and know which host it was — the gate compares
> `runtime` and declines to compare a wall-time metric across two machines.

```powershell
# 1. Run BenchmarkDotNet over both classes, exporting the JSON the capture reads.
dotnet run -c Release --project benchmarks/Fuaran.UI.Ops.Benchmarks -- --filter '*' --exporters json

# 2. Fold the run into the committed artifacts. Each `capture` reads the same
#    artifacts directory; `op` and `render` additionally run their off-hot-path
#    measurements in-process.
dotnet run -c Release --project benchmarks/Fuaran.UI.Ops.Benchmarks -- capture apply  benchmarks/Fuaran.UI.Ops.Benchmarks/BenchmarkDotNet.Artifacts
dotnet run -c Release --project benchmarks/Fuaran.UI.Ops.Benchmarks -- capture op     benchmarks/Fuaran.UI.Ops.Benchmarks/BenchmarkDotNet.Artifacts
dotnet run -c Release --project benchmarks/Fuaran.UI.Ops.Benchmarks -- capture render

# A second positional argument writes elsewhere — that is how CI produces a
# CURRENT measurement to gate against the committed baseline:
dotnet run -c Release --project benchmarks/Fuaran.UI.Ops.Benchmarks -- capture apply <artifacts> /tmp/apply-current.json
```

`capture` sets `status`, stamps `captured_at_utc` and stamps `runtime` **from the run
itself**, so the host can never be mis-transcribed or forgotten. A metric the run did
not produce stays `null` and the artifact stays `pending`, with the unfilled ids
listed and a non-zero exit — a partial capture is never dressed up as a full one.

The individual sub-measurements remain available for inspection, and print already
keyed by metric id:

```powershell
dotnet run -c Release --project benchmarks/Fuaran.UI.Ops.Benchmarks -- hit-rate
dotnet run -c Release --project benchmarks/Fuaran.UI.Ops.Benchmarks -- render-alloc 20000
dotnet run -c Release --project benchmarks/Fuaran.UI.Ops.Benchmarks -- append-rate 20000
```

The pending templates are regenerated with `-- emit-template` / `-- emit-op-template` /
`-- emit-render-template` after a catalogue change.

Those three render families are the allocators [Phase 207](../../../../roadmap/phases/207-renderer-hot-path-allocation-reduction.md)
removed from the render path: the reactive subscription walk, the live-store merge
that precedes it, and the per-node class + ARIA-id vocabulary. Every edit that phase
made is OUTPUT-IDENTICAL, so the test suite cannot see a regression in any of them —
`alloc_b` is the number that can. (The SHAPE of those call sites is locked separately,
by `Fuaran.UI.Tests/HotPathVocabularyTests.fs`.)

## What the captured numbers are worth — read this before setting a budget

The baselines committed here were captured on a **fixed x64 reference host**, recorded
in each artifact's `runtime`. Two things about them are load-bearing downstream:

- **Wall-time is reproducible to about 20%, allocation to about 0%.** Two runs of this
  unchanged tree, minutes apart on the reference host, differ by up to 19.1% on
  `mean_ns` and by 0.000% on `alloc_b`. BenchmarkDotNet's own reported standard
  deviation (0.3–2.7%) is *within*-run scatter and is not the figure a gate has to
  clear. The consuming gate budgets the two units separately for exactly this reason;
  the full measurement is in that gate's `BUDGETS.md`.
- **A baseline is a claim about a machine.** `capture` stamps the host, and the gate
  refuses to compare a wall-time metric across two of them. Recapturing on different
  hardware is a legitimate act — recapturing without saying so was not possible any
  more.

The artifact shape is the cross-repo contract in
[`PERF_BASELINE_SCHEMA.md`](PERF_BASELINE_SCHEMA.md).
