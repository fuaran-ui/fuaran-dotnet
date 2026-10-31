# Fuaran.UI.Renderer.Benchmarks (Phase 2529)

The Fable client render path, timed in a real browser. Three surfaces, four operations, a breakdown per stage.
The reading it produced, with the method and the decisions taken on it, is
[`docs/CLIENT-RENDER-COST.md`](../../docs/CLIENT-RENDER-COST.md).

Not packed, and not part of the gate. The solution build compiles `Surfaces.fs` on .NET; the measurement in
`Bench.fs` exists only under Fable. It touches no renderer source.

```powershell
pwsh ./run.ps1 -Out ./reading.json -Samples 30
pwsh ./run.ps1 -SkipBuild -Out ./second.json -Samples 30 -Compare ./reading.json
```

The second form compares two readings against the stated tolerance and fails if they disagree. Take a reading on
a quiet machine: load moves these figures by more than the tolerance.

## The operations

| Key | What one sample is |
|---|---|
| `fullRender` | A first mount through `renderWithSources`, with the fragment memo cleared |
| `writeReactive` | One store write on a surface mounted through `renderStateReactive` |
| `writePlain` | The same write on a surface mounted through `renderWithSources` |
| `structural` | One host re-render that appends a child, or removes it again |

## The stages

| Key | Meaning |
|---|---|
| `construct` | `renderWithSources` returning its element tree, timed in place |
| `reactRender` | The Profiler's `actualDuration`. On a host render this is nested components and reconciliation. On a reactive write the surface is itself a component, so construction is INSIDE it |
| `constructOffPath` | The same tree constructed once more after the commit, which splits a reactive write's render phase |
| `commit` | The `flushSync` time left after `fanOut` and `reactRender` |
| `layout` | Forcing layout after the commit. Paint is not measured |
| `fanOut` | The store's `Set`, up to the point React starts |
| `commits` | Profiler commits in the sample |
| `domChanged` | Whether the container's text moved |

`isolated` holds what the renderer does not expose in place, measured over the surface's own inputs: `keyWalks`,
`contextCreate`, `fragmentExpansionCold`, `fragmentExpansionMemo`, `transformEvaluation`, `chartLowering`,
`chartSinkSanitise`, `constructWhole`, `constructReaders` (the subtrees that depend on the write, under the
whole tree's context) and `constructWithoutCharts`, which calibrates the chart figures on `sites-24`.

`mutationsPerUpdate` counts DOM mutation records for one update, tallied by kind, in a separate unsampled pass,
because an observer is a cost on the path it watches. `FUARAN_BENCH_TRUSTED_TYPES=off` runs with the Trusted
Types API absent.
