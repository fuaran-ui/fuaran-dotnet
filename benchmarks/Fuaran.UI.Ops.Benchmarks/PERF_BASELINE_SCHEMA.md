# Perf baseline artifact schema (Wave T perf-gate contract)

The single source of truth for the JSON shape produced by the perf-measurement
harnesses (Phase 200 apply / re-derivation, Phase 202 render latency) and
consumed by the perf release-gate (Phase 201, which lives in a separate
downstream repo).

The contract is **the schema, not the code**: the producer side (`Baseline.fs` in
this project) writes it; the gate side re-declares a matching reader via
`System.Text.Json.JsonDocument`. Neither side pulls an F#-aware serializer
dependency. Keep this document and both implementations in lockstep — a field
change is a cross-repo breaking change.

## Shape

```json
{
  "schema_version": 1,
  "artifact": "apply-rederivation" | "render-latency" | "op-append",
  "status": "pending" | "captured",
  "captured_at_utc": "<ISO-8601 UTC, or \"\" while pending>",
  "runtime": { "dotnet": "<sdk>", "os": "<os>", "cpu": "<cpu>" },
  "metrics": [
    { "id": "<dotted.metric.id>", "value": <number|null>, "unit": "ns|B|ms|ratio", "note": "<human note>" }
  ]
}
```

### Field semantics

| Field | Meaning |
|---|---|
| `schema_version` | Bumped only on a breaking shape change. The gate **refuses** a baseline whose version it does not understand (no silent pass). |
| `artifact` | Which producer emitted it. The gate's budget table is keyed per artifact. |
| `status` | `pending` = IDs/units declared, no numbers captured yet. The gate treats `pending` as **"no baseline" → hard fail** (Phase 201's no-silent-pass discipline). `captured` = a real run filled every `value`. |
| `captured_at_utc` | When the capturing run happened. Empty while `pending`. |
| `runtime` | The machine the numbers were captured on — load-bearing: a baseline captured on a different CPU is not comparable, so the gate surfaces a mismatch rather than comparing blind. |
| `metrics[].id` | The stable key the Phase 201 **budget table** keys against. Dotted, lowercase, scenario-suffixed. |
| `metrics[].value` | The captured number, or JSON `null` while `pending` (JSON has no `NaN`). |
| `metrics[].unit` | `ns` (mean wall-time/op), `B` (allocated bytes/op), `ms` (render/TTFP latency), `ratio` (unit-free fraction, e.g. memo hit-rate). |

## Lifecycle

1. **Build (this opener):** the `pending` template ships committed
   (`apply-rederivation-baseline.json` here; the render-latency analogue in
   Phase 202). Declares the metric IDs the gate budgets against. Regenerate it
   with `dotnet run -c Release --project . -- emit-template`.
2. **Capture (deferred run):** an operator runs the benchmark on a fixed
   machine, fills every `value`, sets `status: captured` + `captured_at_utc` +
   `runtime`, and commits the refreshed artifact. **This is the deferred step** —
   it is a benchmark run, out of the build-only opener's scope.
3. **Gate (Phase 201):** on each release, a fresh measurement is compared to the
   committed `captured` baseline against the budget table; a regression beyond
   budget fails CI. A `pending` or absent baseline fails loudly.

## Declared metric IDs (apply / re-derivation, Phase 200)

Per corpus size `{Small, Medium, Large}`:

- `apply.full.<Size>.mean_ns` — bare `FragmentApply.apply` (no cache); the cold cost.
- `apply.memoised_hit.<Size>.mean_ns` — `Engine.Apply` warm-cache structural HIT.
- `apply.incremental.<Size>.mean_ns` — `Engine.Reapply` single value edit (the Phase 183 win).
- `apply.full.<Size>.alloc_b` — allocated bytes per bare apply.
- `apply.incremental.<Size>.alloc_b` — allocated bytes per incremental reapply.

Plus one session-level metric:

- `memo.hit_rate.edit_session` — memo reuse fraction over a representative value-edit session.

The Phase 202 render-latency catalogue (`render.ttfp.<Size>.ms`,
`render.full.<Size>.ms`, …) is declared in that phase's producer and documented
in its own measurement surface, sharing this envelope.

## Declared metric IDs (op-stream write path, Wave-T op-append follow-on)

A second producer artifact, `op-append`, shares this envelope. It benchmarks the
op-stream WRITE path the Phase 200 apply harness left unmeasured: canonical
encode → SHA-256 hash-chain → durable append. Metrics are keyed per op shape
`{UpdateProp, InsertChild, Batch16}`:

- `opstream.encode.<Shape>.mean_ns` / `.alloc_b` — `CanonicalJson.encodeOp` alone.
- `opstream.hash.<Shape>.mean_ns` / `.alloc_b` — `HashChain.computeHash` (encode + SHA-256).
- `opstream.build_record.<Shape>.mean_ns` / `.alloc_b` — hash + materialise the `OpRecord`.
- `opstream.append.<Shape>.mean_ns` / `.alloc_b` — `InMemorySink.Append` per op (measured off the BenchmarkDotNet hot path by `AppendRate.measure`).

**Framing (pre-publication):** this baseline's value is **absolute** — confirm
the generated codec + the hash pre-image are cheap and catch pathological
allocation. It is not (yet) wired into the Phase 201 regression gate; arming a
regression budget is a publication-time concern.
