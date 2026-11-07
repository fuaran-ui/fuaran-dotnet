# Fuaran.UI.Renderer.Benchmarks (Phase 2529)

The Fable client render path, timed in a real browser. Four surfaces, four operations, a breakdown per stage.
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
| `boundaryRenders` | Phase 2530: reader-boundary component renders one write cost, from `Render.ReaderBoundaries.renderCount` |

`isolated` holds what the renderer does not expose in place, measured over the surface's own inputs: `keyWalks`,
`contextCreate`, `fragmentExpansionCold`, `fragmentExpansionMemo`, `transformEvaluation`, `chartLowering`,
`chartSinkSanitise`, `constructWhole`, `constructReaders` (the subtrees that depend on the write, under the
whole tree's context) and `constructWithoutCharts`, which calibrates the chart figures on `sites-24`.

`checks` (Phase 2530) holds, per write operation, `plannedBoundaries` (what
`Render.ReaderBoundaries.rerenderedBy` says one write of the surface's key re-renders), the fewest and most
`boundaryRenders` any sample measured, `matchesFresh` (the surface after its last write against a fresh render of the
same stores) and `focusKept` (focus held in the surface's first input across every write; `null` with no input).
`drive.mjs` refuses a run whose `writeReactive` measured a count other than the planned one on any sample, differs from
the fresh render, or lost focus. `writePlain` records the same checks and is expected to fail `matchesFresh` and to count no boundary: the plain
entry subscribes nothing, which is what makes the probe able to fail.

`mutationsPerUpdate` counts DOM mutation records for one update, tallied by kind, in a separate unsampled pass,
because an observer is a cost on the path it watches. `FUARAN_BENCH_TRUSTED_TYPES=off` runs with the Trusted
Types API absent.
