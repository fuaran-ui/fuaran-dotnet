# Client render cost

Where a Fable client update spends its time, measured in a real browser before any of it is changed.
Phase 2529. This is the **before-reading**: Phase 2069 and Phase 2068 are unshipped, so every figure is the
renderer as it stands at commit `eb61f81`. Their outcomes quote against this page, and the page is re-taken
after they land rather than edited.

## The decisions

- **Phase 2530: GO, with one amendment.** A write with one reader re-renders everything: 183 ms to 195 ms on
  the 40-module shell and 75 ms on the catalog, where the reader's own subtree constructs in 2.1 ms and 0.03 ms.
  No other open phase removes that. **The amendment:** the map must key on what a pipeline READS, not on what it
  DECLARES. The 24-site tree's sites read one chip each and declare all four, so a map projected from today's
  key walk subscribes 24 sites to a chip 6 of them read, and the phase's first acceptance line cannot be met.
- **F5 (subtree memoisation or keyed reuse): GO, sized after Phases 2068 and 2069.** A host-driven update
  rebuilds the whole tree, and Phase 2530 does nothing for that path. One added child on the shell costs 176 ms
  to 195 ms, 122 ms to 136 ms of it construction, against a 16.7 ms frame and a consumer's published
  per-dispatch construction ceiling of 4.4 ms. Chart lowering is 72 ms of that construction and Phase 2068 owns
  it. The residue, about 50 ms of construction plus about 40 ms of commit, is still several frames, which is the
  case for F5. Size the phase on the reading taken after 2068, not on this one.

## The method

Run from this repository, on any machine with Node 22, the .NET SDK and Edge:

```powershell
pwsh ./benchmarks/Fuaran.UI.Renderer.Benchmarks/run.ps1 -Out ./reading.json -Samples 30
pwsh ./benchmarks/Fuaran.UI.Renderer.Benchmarks/run.ps1 -SkipBuild -Out ./second.json -Samples 30 -Compare ./reading.json
```

`FUARAN_BENCH_BROWSER` names another Chromium channel. The project's
[README](../benchmarks/Fuaran.UI.Renderer.Benchmarks/README.md) defines every stage.

The bundle is the Fable output under Vite's production mode, with React 19.2.6's production profiling build and
the reference stylesheet, in headless Chromium at a 1440x900 viewport. The page is cross-origin isolated, so the
clock resolves 5 microseconds, and a collection runs before every sample. Each operation has 15 warm-up rounds
and 30 samples.

**Reference machine:** Intel Core i7-8650U at 1.90 GHz, 8 logical cores, 16 GiB, Windows 11 build 26300, Edge
156.0.4314.50, Node 22.20.0, on mains power. Take a reading only below 30 percent machine load: the first
attempt ran at 76 percent load and read 1.5 to 2 times these figures.

**Tolerance:** every median of every stage within 25 percent or 0.5 ms, whichever is larger. `compare.mjs` checks
all 118. Runs 1 and 2 below agree on all 118. An earlier run on the same commit disagreed with each of them on
one cell, the catalog's `keyWalks` (1.62 ms against 0.93 ms and 0.97 ms), a 0.7 ms stage just beyond the floor.
The readings are [run 1](../benchmarks/Fuaran.UI.Renderer.Benchmarks/reading-run1.json) and
[run 2](../benchmarks/Fuaran.UI.Renderer.Benchmarks/reading-run2.json).

**What a run refuses.** `drive.mjs` fails a run in which a full render built under 100 elements, a reactive write
committed nothing or left the DOM unchanged, a structural change moved no element, or the clock or collector
was not as stated.

**What the figures do not include.** Paint is not measured. Stages are medians, so they do not sum to a total.
Fragment expansion, Transform evaluation, chart lowering and sink sanitising are measured off the render path
over the surface's own inputs, because this phase changes no renderer source. They sit INSIDE construction and
are not added to it. Off-path figures run high: on the 24-site tree the three chart figures sum to 57.6 ms where
removing the charts takes 47 ms off construction. Read them as shares, not as a ledger.

## The surfaces

| Surface | Nodes | DOM elements | Charts | Fragment refs | Written key | Subtrees that depend on it |
|---|---|---|---|---|---|---|
| `sites-24`: Phase 1761's generated tree, one 2,000-row table feeding 24 Transform charts | 77 | 1,383 | 24 | 24 | a filter chip | 6 of 24 (24 subscribed) |
| `catalog`: every kind in `samples/catalog`, once per tone | 604 | 3,128 | 7 | 0 | a state key | 1 of 218 |
| `shell-40`: 40 modules, each six state metrics, a 20-row table, a form and a chart, all mounted | 447 | 6,683 | 40 | 40 | a state key | 1 of 41 |

`shell-40` is deliberately the worst case: a host that mounts one module at a time pays a fortieth of its
construction. A module's subtree constructs in 2.1 ms, against 2.4 ms for a fortieth of the whole.

## The reading

Medians in milliseconds, run 1 / run 2.

### One full render (plain entry, first mount, fragment memo cold)

| Surface | Total | Construction | React render | Commit | Layout |
|---|---|---|---|---|---|
| `sites-24` | 81.8 / 79.8 | 68.8 / 67.0 | 5.5 / 5.4 | 0.4 / 0.4 | 6.8 / 6.5 |
| `catalog` | 129.5 / 115.6 | 56.0 / 48.9 | 49.9 / 44.9 | 1.7 / 1.5 | 20.3 / 17.7 |
| `shell-40` | 276.6 / 268.3 | 123.6 / 118.4 | 115.3 / 113.5 | 2.2 / 2.3 | 34.1 / 32.4 |

### One write under `renderStateReactive`

| Surface | Total | Fan-out | Render phase (construction inside) | Commit | Layout | DOM records |
|---|---|---|---|---|---|---|
| `sites-24` | 76.1 / 75.0 | 0.05 / 0.04 | 65.2 / 64.6 | 5.0 / 4.2 | 5.5 / 5.2 | 24 |
| `catalog` | 76.8 / 74.5 | 0.05 / 0.05 | 56.0 / 55.2 | 15.1 / 15.8 | 4.0 / 3.8 | 449 |
| `shell-40` | 195.0 / 183.3 | 0.05 / 0.05 | 137.8 / 130.3 | 41.7 / 43.5 | 11.6 / 11.4 | 401 |

### One write under the plain entry

| Surface | Total | Fan-out | Commits | DOM changed |
|---|---|---|---|---|
| `sites-24` | 0.04 / 0.04 | 0.02 / 0.02 | 0 | no |
| `catalog` | 0.06 / 0.06 | 0.03 / 0.03 | 0 | no |
| `shell-40` | 0.07 / 0.07 | 0.03 / 0.03 | 0 | no |

### One structural change (one child appended, host re-render)

| Surface | Total | Construction | React render | Commit | Layout | DOM records |
|---|---|---|---|---|---|---|
| `sites-24` | 79.4 / 77.4 | 68.5 / 67.1 | 0.3 / 0.3 | 4.7 / 4.8 | 5.7 / 5.3 | 25 |
| `catalog` | 78.9 / 72.0 | 56.3 / 49.8 | 2.0 / 1.9 | 16.0 / 15.0 | 4.1 / 3.9 | 449 |
| `shell-40` | 194.7 / 176.0 | 136.2 / 122.3 | 2.5 / 2.4 | 41.9 / 40.3 | 11.7 / 10.8 | 401 |

### Inside construction, per tree, off the path

| Stage | `sites-24` | `catalog` | `shell-40` |
|---|---|---|---|
| Whole construction | 47.5 / 49.5 | 33.7 / 37.3 | 99.8 / 98.0 |
| Chart lowering | 36.0 / 36.3 | 9.8 / 11.6 | 73.2 / 71.8 |
| Transform evaluation | 19.4 / 19.5 | 0.03 / 0.03 | 0.4 / 0.4 |
| Sanitising chart SVG at the sink | 2.0 / 2.2 | 0.9 / 1.0 | 5.5 / 5.4 |
| Fragment expansion, uncached | 1.6 / 1.6 | none | 2.0 / 2.4 |
| Fragment expansion, memoised | 0.07 / 0.08 | none | 1.9 / 1.6 |
| `RenderContext.create` (fragment and seed walks) | 2.4 / 2.4 | 9.1 / 10.5 | 13.7 / 12.3 |
| The four key walks | 0.6 / 0.6 | 0.9 / 1.0 | 1.9 / 1.9 |
| The subtrees that depend on the write | 8.6 / 8.5 | 0.03 / 0.04 | 2.1 / 2.1 |

`sites-24` with its charts replaced by a placeholder constructs in 2.0 ms.

## What it says

- **A store write is cheap and what follows it is not.** Fan-out is 0.05 ms everywhere. Nothing is gained by
  optimising notification.
- **Under the reactive entry a write costs a full render.** The render phase of a one-reader write is within
  20 percent of constructing the page on all three surfaces.
- **Charts are the largest single stage.** About 3 ms a chart, unmemoised, on every render of every chart. On
  the shell that is 40 percent of a write. Phase 2068 owns it.
- **React does not keep the DOM work partial.** One changed value writes 401 DOM records on the shell. 360 are
  React re-assigning `name` and `type` on 120 inputs, which it does on any input update. The other 40 are every
  chart's SVG reparsed, and that one is ours: React 19's update path assigns `innerHTML` whenever the
  `dangerouslySetInnerHTML` prop object is new, without comparing `__html`, and the renderer builds a new object
  each time. The same run with `trustedTypes` removed gives the same count, so it is not the `TrustedHTML`
  wrapper.
- **The plain entry has no reader subscriptions.** A write there updates nothing until the host re-renders.
  That is a correction to the common description of this entry, not a defect: Phase 2530's goal text needs the
  same correction.

**For Phase 2069's acceptance.** Read from source, not from an instrumented count: one write under the reactive
entry runs no key walk, because `renderStateReactive` walks once per call, and runs two tree walks inside
`RenderContext.create` (2.4 ms, 10 ms and 13 ms). Each host render of the reactive entry adds the four key walks
(0.6 ms, 1 ms and 1.9 ms).

## Costs no open phase owns

Two candidates, neither fixed here:

1. **A raw sink whose markup did not change is reparsed on every re-render.** Commit is 41 ms on the shell and
   5 ms on `sites-24`, where reparsing is almost all of it. A stable prop object per markup string would stop
   it. Phase 2069 changes where sinks sanitise, not this.
2. **The fragment memo does nothing for more than 32 refs of one body.** Its per-body prefix bound is 32, so a
   sequential render of 40 refs evicts each entry before its next use: 1.6 ms to 1.9 ms memoised against
   2.0 ms to 2.4 ms uncached on the shell, and 0.07 ms against 1.6 ms on `sites-24`. Small today, and it grows
   with the ref count.
