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

Since the cut (Phase 2645) the DAG is `Fuaran.Core`'s: this package defines no
node, address, verifier, topology or retention of its own.

| Type / module | Role |
|---|---|
| `DagOpRecord<'Msg>` | A Core `DagNode<StreamEntry<'Msg>>` with the stream it is stored in. The op is the linear chain's envelope (op, timestamp, prompt, result), so a lifted node's op is byte-identical to its linear op. The address is Core's `Dag.nodeId` under SHA-256 (`DagOpRecord.nodeId`). A merge node is an ordinary node whose op is its reconciliation script. |
| `DagOpRecord.ofLinear` / `DagLift` | Embed a linear history as a single-parent branch; the lift verifies the segment first and returns a recomputable `LiftReceipt`. |
| `DagVerify` | Integrity: Core's `Dag.firstBreak` over a closed set, or the store-resolved form the sinks' read paths use. |
| `DagRetention` | GC roots and the retention policy over Core's `Dag.prunable`. |
| `IDagOpStreamSink<'Msg>` | Durable superset interface: admission by address recompute and parent presence, an atomic `TryAdvanceHead` CAS on the trunk head, and a pruned-node skeleton (`DagSkeletonNode`) beside the live set. |

## Posture

- **Opt-in.** A consumer that references only `Fuaran.UI` / `.Renderer` / `.Ops`
  / the linear `.OpStream.*` packages pulls **none** of this binary or API. The
  merge engine (`Fuaran.UI.OpStream.Dag.Merge`) *requires* `IDagOpStreamSink`,
  so DAG behaviour is structurally unreachable from the linear path.
- **FGP 2 / FGP 6.** Depends on `FSharp.Core` + `Fuaran.UI` + `Fuaran.UI.Ops` +
  the linear `Fuaran.UI.OpStream.Abstractions` + `Fuaran.Core.OpStream.Dag` only. No orchestration-private
  dependency; Apache-2.0-clean alongside the linear abstractions.

Apache-2.0 licensed — see the repo [LICENSE](../../LICENSE).
