# Fuaran.UI.OpStream.Dag.InMemory

Per-process, `Dictionary`-backed implementation of `IDagOpStreamSink<'Msg>`
(from `Fuaran.UI.OpStream.Dag.Abstractions`).

The single-process counterpart of the Sqlite DAG sink: a **guarded ref** is the
`TryAdvanceHead` compare-and-swap primitive. Records live in a per-stream
`hash → DagOpRecord` map; branch appends never contend (a writer just adds a
node whose parents it chose), so the only serialised operation is the CAS on the
per-stream trunk head. A pruned node moves to the stream's skeleton.

It is also an `IDagOpStreamIndexedSink<'Msg>` (Phase 2716): each stream holds
Core's reachability index (`Dag.Reach`) beside its node map, built at the first
ancestry question and extended by every admitted record, so `Reachable` and a
merge through `DagMerge.mergeWith` cost what they cost a caller holding the DAG.
Records are verified when they are added (the address recomputes, every parent is
present) and trusted when read; there is no backing store, so no load
(`docs/DECISIONS.md` D32). `Reverify` re-checks a stream on demand.

Useful for tests and ephemeral preview environments. Opt-in (rung-4): a consumer
that never references the DAG packages pulls none of this.

Apache-2.0 licensed — see the repo [LICENSE](../../LICENSE).
