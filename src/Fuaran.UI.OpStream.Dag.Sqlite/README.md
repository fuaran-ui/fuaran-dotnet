# Fuaran.UI.OpStream.Dag.Sqlite

`Microsoft.Data.Sqlite`-backed implementation of `IDagOpStreamSink<'Msg>`
(from `Fuaran.UI.OpStream.Dag.Abstractions`).

The durable counterpart of the in-memory DAG sink. Records live in a
`dag_op_record` table keyed by `(stream_id, hash)`; the trunk head lives in a
`dag_head` table. The `TryAdvanceHead` compare-and-swap is a **conditional
`UPDATE dag_head SET head = @new WHERE stream_id = @s AND head = @expected`** —
atomic at the statement level, so two racing advancers from the same expected
head leave exactly one winner. The genesis advance (`expected = None`) is an
`INSERT … ON CONFLICT DO NOTHING` whose rows-affected distinguishes "head unset"
from "head already set".

Add is idempotent by content address; a hash collision with differing content
is rejected. Topology queries (`Parents` / `Reachable` / `Lca`) load the
stream's `(hash, parents)` rows and delegate to the pure `DagTopology`
algorithms. Tombstone drops the op payload (`op_json` reset to an empty batch,
`outcome_hash` cleared, `tombstoned = 1`) while preserving the hash + parent
links, so the chain stays verifiable.

Opt-in (rung-4): a consumer that never references the DAG packages pulls none of
this.

Apache-2.0 licensed — see the repo [LICENSE](../../LICENSE).
