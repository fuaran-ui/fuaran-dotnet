# Fuaran.UI.OpStream.Dag.Merge

The **M1 branch-merge engine** for the Fuaran op-stream DAG (opt-in, rung-4).

M1 is the *determinism-trivial* subset of merge: **fast-forward** and
**disjoint-`(NodeId, facet)` auto-merge** only. Full 3-way merge with primacy
and multi-agent reconciliation is the gated follow-on (Phase 179).

## What ships here

| Module | Role |
|---|---|
| `DagReplay` | Reconstruct a tree at a head by folding ops along the primary-parent spine. A merge node's op is the replay delta from its primary parent, so the spine fold reconstructs the merged tree. |
| `TreeMerge` | Structural 3-way merge under the disjointness rule. Facets: `content` (own kind-fields + style + state + accessibility, closure-safe) and `children` (ordered child-id list). Disjoint pure inserts into the same parent auto-merge with a **NodeId-canonical-bytes** tie-break (no wall-clock). Overlap returns the contended cells. |
| `DagMerge` | Orchestration: LCA → ambiguous (Phase 179) / none / fast-forward / disjoint auto-merge. Builds the **merge node** committed to the canonical **outcome-tree hash** (not the op-path), so two hosts agree iff they reach the same tree. `commitMerge` adds the node + advances the trunk under the `TryAdvanceHead` CAS. |

## Determinism contract

Two hosts (e.g. the F# tier and the TypeScript reference implementation)
computing an M1 merge over the same base + branches produce a **byte-identical
merged tree and merge-node hash**: the merge is a pure function of the three
trees, the tie-break is NodeId canonical bytes, and the merge-node hash folds in
the canonical encoding of the resulting tree.

Requires `IDagOpStreamSink` (from `Fuaran.UI.OpStream.Dag.Abstractions`), so
the merge surface is structurally unreachable from the linear op-stream path.

Apache-2.0 licensed — see the repo [LICENSE](../../LICENSE).
