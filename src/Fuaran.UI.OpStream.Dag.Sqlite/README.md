# Fuaran.UI.OpStream.Dag.Sqlite

`Microsoft.Data.Sqlite`-backed implementation of `IDagOpStreamSink<'Msg>`
(from `Fuaran.UI.OpStream.Dag.Abstractions`).

The durable counterpart of the in-memory DAG sink. Records live in a
`dag_node` table keyed by `(stream_id, hash)`; the trunk head lives in a
`dag_head` table. The `TryAdvanceHead` compare-and-swap is a **conditional
`UPDATE dag_head SET head = @new WHERE stream_id = @s AND head = @expected`** —
atomic at the statement level, so two racing advancers from the same expected
head leave exactly one winner. The genesis advance (`expected = None`) is an
`INSERT … ON CONFLICT DO NOTHING` whose rows-affected distinguishes "head unset"
from "head already set".

Add is idempotent by content address; a hash collision with differing content
is rejected, and admission recomputes the address (Core's `Dag.nodeId`) and
checks every parent is present. The
schema is versioned (`dag_schema`; a pre-2645 `dag_op_record` table is refused by
name, to be re-minted): live nodes in `dag_node`, and a pruned node moves to
`dag_skeleton` with its address and parent links, never while a live node names
it as a parent.

**The read contract (Phase 2716, `docs/DECISIONS.md` D32): verified on write and
at load, trusted on read.** The sink holds each stream it is asked about: its live
records and Core's reachability index, built at the first ancestry question and
extended by every admitted record, so `Reachable` and a merge through
`DagMerge.mergeWith` cost what they cost a caller holding the DAG. A stream is
LOADED (read and verified in full, unless the host named `LoadVerification.Off`)
the first time a read touches it in a sink instance. Another writer on the same
file is detected at the next read by the stream's row counts: records it added are
read and verified as at load, and a prune re-loads the stream. A row changed in
place after the load, by something other than this sink, is not detected by a
read; `Reverify` (`IDagOpStreamIndexedSink`) re-reads and re-checks the stream on
demand and refuses it by name.

Opt-in (rung-4): a consumer that never references the DAG packages pulls none of
this.

Apache-2.0 licensed — see the repo [LICENSE](../../LICENSE).
