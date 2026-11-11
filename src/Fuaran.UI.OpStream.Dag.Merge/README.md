# Fuaran.UI.OpStream.Dag.Merge

The **merge** of the Fuaran op-stream DAG (opt-in, rung-4). Since the cut (Phase
2645) the UI DAG is `Fuaran.Core`'s DAG over the op envelope, and its merge is a
thin reconciliation policy over Core's conflict report and Core's fold. There is
no tree-level 3-way merge and no merge-outcome identity.

## What ships here

| Module | Role |
|---|---|
| `DagReconcile` | The policy (`WIRE_FORMAT.md` §34): Core's `Dag.conflictsIn` over the tier's base-aware footprint; an agreement applies as no change, two appends under one parent are ordered by one declared child-order rule, a reorder beside a change of its list is rebased, every other pair refuses (`DeleteModify` or `Conflict`, a byte-stable envelope). Both fold orders must reach one tree. An optional precedence side re-asserts its refused ops as script. |
| `DagFold` | The tier's binding of Core's replay (`Dag.tryReplayTo`), checkpoints (`Dag.checkpointAt`, `Dag.replayFrom`), compaction (`Dag.compactAt`) and checkpoint verification: the policy's replay witness, SHA-256, the canonical tree encoding. |
| `DagMerge` | The one merge (`mergeDag`, over a `MergeContext`): fast-forward, the policy, the validator gate, and a merge node minted by Core (`Dag.merge`) whose op is the script — checked to replay to the derived tree before it is handed back. `commitMerge` / `mergeIntoTrunkWith` store it under the trunk CAS. |
| `ValidatorGate` | Core's introduced-findings gate over a derived merge (`Lenient` / `Diagnostic` / `Gated`). |
| `GuestReplay` / `GuestConvergence` | A mounted guest's interior replayed from its own stream; a guest stream merged back into its host, the host taking precedence. |
| `BatchAccept` | Partial acceptance of a batch with its dependency closure. |

## Determinism contract

Two hosts merging the same two heads derive the same tree, refuse with the same
envelope, and mint the same merge node: the node's address is Core's `Dag.nodeId`
over its sorted parents, its actor and its envelope, and its op is the policy's
script. The `foldFixtures` family of the merge-conformance corpus pins every
verdict.

Requires `IDagOpStreamSink` (from `Fuaran.UI.OpStream.Dag.Abstractions`), so the
merge surface is structurally unreachable from the linear op-stream path.

Apache-2.0 licensed — see the repo [LICENSE](../../LICENSE).
