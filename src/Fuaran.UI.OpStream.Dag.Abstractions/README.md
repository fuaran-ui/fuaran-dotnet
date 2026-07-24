# Fuaran.UI.OpStream.Dag.Abstractions

The **opt-in, rung-4** branching-DAG generalisation of the Fuaran op-stream.

The linear op-stream (`Fuaran.UI.OpStream.Abstractions`) chains applied `TreeOp`s
into a SHA-256 hash-chain — one parent per record, one head. This package
generalises the *temporal* graph to a content-addressed **Merkle-DAG**: records
carry `Parents: string list` (0..n parent hashes) instead of a single
`PreviousHash`, so a history can branch and merge.

The *spatial* graph is untouched: the `Node<'Msg>` tree stays a tree and the
10-op `TreeOp` algebra is unchanged. Branch and merge are properties of how
op-records **link**, never new `TreeOp` cases.

## What ships here

| Type / module | Role |
|---|---|
| `DagOpRecord<'Msg>` | Content-addressed, multi-parent op-record. `Parents` in author order (primary first); hash sorts them so merge identity is parent-order-independent. Merge nodes commit to an **outcome hash** (the canonical hash of the resulting tree), not the op-path. |
| `DagOpRecord.ofLinear` | Embeds a linear `OpRecord` history as a single-parent DAG — the degenerate-equivalence path (same resulting tree, verifiable chain, no data migration). |
| `DagTopology` | Pure `reachable` / `isAncestor` / `lca` over a `getParents` lookup. `LcaResult` is `None` / `Unique` / `Ambiguous` (the last = "needs 3-way merge — Phase 179"). |
| `DagVerify` | Integrity check: parent-linkage + content-address recompute. Tombstoned records keep their hash (payload pruned) and the chain still verifies. |
| `IDagOpStreamSink<'Msg>` | Durable superset interface: content-addressed `Add`, atomic `TryAdvanceHead` CAS on the trunk head, topology queries, and tombstone pruning. |

## Posture

- **Opt-in.** A consumer that references only `Fuaran.UI` / `.Renderer` / `.Ops`
  / the linear `.OpStream.*` packages pulls **none** of this binary or API. The
  merge engine (`Fuaran.UI.OpStream.Dag.Merge`) *requires* `IDagOpStreamSink`,
  so DAG behaviour is structurally unreachable from the linear path.
- **FGP 2 / FGP 6.** Depends on `FSharp.Core` + `Fuaran.UI` + `Fuaran.UI.Ops` +
  the linear `Fuaran.UI.OpStream.Abstractions` only. No orchestration-private
  dependency; Apache-2.0-clean alongside the linear abstractions.

Apache-2.0 licensed — see the repo [LICENSE](../../LICENSE).
