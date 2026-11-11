# Phase 2648 — the DAG merge re-baselined over Core's fold (after the Phase 2645 cut)

Host: Intel Core i7-8650U (8 logical processors), Windows 11 (10.0.26300), .NET SDK 10.0.203, Release build of the
cut's tree (`68e12156`). This was **not an idle machine**: a sibling session was building and testing four other host
repositories in parallel, and Docker Desktop and a browser were resident (CPU load read 14% just before run 2). Treat
differences under ~25% between the two runs as noise. Allocation is deterministic and repeats to within a few bytes.

## `merge-rate 5 1000,5000,20000`: two ten-record branches over n shared records, two runs

| scenario | n = 1,000 | n = 5,000 | n = 20,000 | alloc at 20,000 |
|---|---|---|---|---|
| `lca` (Core's `Dag.mergeBase`, DAG loaded once) | 28.5 / 23.1 ms | 47.2 / 69.6 ms | 359.4 / 451.6 ms | 168.1 MB |
| `sink` (`DagMerge.merge`: load, merge base, replays from genesis, reconcile, mint) | 139.5 / 136.0 ms | 432.5 / 565.2 ms | 2,371.6 / 2,884.2 ms | 2,121.6 MB |
| `sink_ckpt` (`mergeWith`, Core checkpoint at the fork) | 46.1 / 51.5 ms | 299.6 / 372.7 ms | 1,976.3 / 2,083.4 ms | 1,085.8 MB |
| `lookups_ckpt` (`mergeDag` over the loaded DAG, same checkpoint) | 40.8 / 39.9 ms | 262.2 / 388.3 ms | 1,789.8 / 1,939.8 ms | 865.9 MB |

**Against Phase 2062's figures, before the cut**, merge cost had been made independent of shared history. `lca` ran
in 0.02–0.04 ms at every n up to 100,000. `mergeLookups` with a fork checkpoint ran in 11–19 ms, flat at 1.43 MB.
After the cut, every scenario grows with n, including the checkpointed ones. At n = 20,000 the merge base alone costs
about 400 ms and allocates 168 MB. Phase 2645 recorded this in Debug as finding F6 (`lookups_ckpt` 49 ms → 2,319 ms;
`mergeBase` 345 ms). The Release figures here are about the same, so the cost is algorithmic and not codegen: Core's
`Dag.mergeBase`, `Dag.betweenOps` and the closure fold walk the whole ancestor set. A checkpoint now saves only the
apply work, roughly 20–30% of the `sink` figure, and not the walks. The remedy F6 recommends is an operator decision
already filed by the cut: a reach-indexed `mergeBase` / `betweenOps` in Fuaran.Core, adopted here afterwards. It is
not a change this benchmark phase makes.

## `merge-snapshot`: the result identity over seeded random histories

838 lines, **sha256 `d4cef8c8e752b40992d7ddb066e2c6be36b0bbc01b570729abcbccda3900b79a`**, written twice and
byte-identical, so the snapshot is deterministic. Line census by result: merge base `unique` 360, `ff` 203,
`refused` 183, `already` 43, `merged` 18, the seeded `history` progress lines, and nine `trunk` CAS lines. There is
no `ambiguous` line: Core's merge base answers one node or none.

The pre-cut snapshot (Phase 2062, sha256 `07f6c781…`, 828 lines) is not comparable line for line. Every content
address moved at the cut, a merge node's address now covers its script rather than its outcome tree, and the tree
merge's `conflict` lines became the reconciliation policy's `refused` lines. This digest is the baseline the next
change to the merge engine is diffed against:

```powershell
dotnet run -c Release --project benchmarks/Fuaran.UI.Ops.Benchmarks -- merge-snapshot merges.txt
Get-FileHash merges.txt -Algorithm SHA256   # D4CEF8C8E752B409…
```

## The derivability snapshot

Retired with the tree merge at Phase 2645 (it measured the reconciliation policy against `TreeMerge.merge3Way`, which
no longer exists). Its last figures are kept in `docs/DAG_STRATEGY.md`.
