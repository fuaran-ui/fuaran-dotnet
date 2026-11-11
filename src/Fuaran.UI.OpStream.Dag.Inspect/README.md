# Fuaran.UI.OpStream.Dag.Inspect

DAG-aware op-stream inspector substrate — the read-only "variation forest" view over
the Fuaran op-stream's branching Merkle-DAG. Opt-in (rung-4): it requires the DAG
packages, so a consumer that never references it pays nothing, and it is structurally
unreachable from the light/linear path.

It is a **derived view** — it reads records + topology and emits renderable models;
it writes no op-stream records and no telemetry (the stream stays the source of truth).

## Surface

| Module | Role |
|---|---|
| `DagGraphModel` | Layered render model — nodes classified `Genesis`/`Linear`/`Merge`, branch-points + leaves, longest-path depth, primary-vs-secondary edges. |
| `DagAudition` | Audition a node by its content-addressed coordinate — reconstruct its snapshot tree (Core's closure fold, `DagFold.replayTo`) + an optional host preview/playback hook. |
| `DagOverlay` | Precedence (the host's per-node predicate) and retention overlay (live / prunable / tombstoned, the last drawn from the sink's skeleton) over the graph. |
| `DagCoordinateDiff` | Diff **any two** DAG coordinates (the generalisation of the linear adjacent-snapshot diff), plus an N-way baseline-vs-many fan-out. |

## Dependencies

`Fuaran.UI` · `Fuaran.UI.Ops` · `Fuaran.UI.OpStream.Abstractions` · `Fuaran.UI.OpStream.Replay`
(the linear `TreeDiff`) · `Fuaran.UI.OpStream.Dag.Abstractions` (the records and the skeleton) ·
`Fuaran.UI.OpStream.Dag.Merge` (`DagFold`, the closure-fold replay). Topology and retention are
Fuaran.Core's (`Dag.ranks`, `Dag.prunable`, `Dag.mergeBase`).

Layering: `Dag.Abstractions` ← `Dag.Merge` ← **`Dag.Inspect`**.
