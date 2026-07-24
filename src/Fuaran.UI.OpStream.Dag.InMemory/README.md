# Fuaran.UI.OpStream.Dag.InMemory

Per-process, `Dictionary`-backed implementation of `IDagOpStreamSink<'Msg>`
(from `Fuaran.UI.OpStream.Dag.Abstractions`).

The single-process counterpart of the Sqlite DAG sink: a **guarded ref** is the
`TryAdvanceHead` compare-and-swap primitive. Records live in a per-stream
`hash → DagOpRecord` map; branch appends never contend (a writer just adds a
node whose parents it chose), so the only serialised operation is the CAS on the
per-stream trunk head. Topology queries (`Parents` / `Reachable` / `Lca`)
delegate to the pure `DagTopology` algorithms.

Useful for tests and ephemeral preview environments. Opt-in (rung-4): a consumer
that never references the DAG packages pulls none of this.

Apache-2.0 licensed — see the repo [LICENSE](../../LICENSE).
