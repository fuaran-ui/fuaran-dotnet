# Op-stream shapes: linear chain vs. branching DAG

Fuaran ships **two** op-stream persistence shapes, and a downstream repo (`Fuaran.Core`) ships a
**third**, minimal one. This is deliberate, not unfinished convergence. This document says which to
use and – for the curious contributor – why the UI's DAG is intentionally *not* built on the Core DAG.

## Which one do I use?

| You are building… | Use | Package(s) |
|---|---|---|
| A single-writer, sequenced, audit-logged stream (server-driven UI, deterministic replay, one authoritative timeline) | **Linear chain** | `Fuaran.UI.OpStream.Abstractions` + `.InMemory` / `.Sqlite` / `.Replay` |
| A multi-agent / branching / eventually-consistent history (concurrent editors, fork-and-merge, offline-then-reconcile) | **Branching DAG** | `Fuaran.UI.OpStream.Dag.*` |

**Default to the linear chain.** It is simpler, cheaper, and covers every single-timeline case. Reach
for the DAG only when two writers genuinely diverge and must be merged – the DAG exists to make
"two hosts that reached the same tree agree" a first-class, verifiable property.

Both shapes share the same op vocabulary (`TreeOp`), the same canonical encoder, and the same
host-side SHA-256 `HashFn`. Moving from linear to DAG is a persistence-layer choice, not a rewrite of
your edit logic.

## Moving a linear stream onto a DAG branch

The two formats stay two formats: a linear stream is not silently re-read as a DAG. To continue a linear
stream as a branch, use the **lift**, `DagLift` in `Fuaran.UI.OpStream.Dag.Abstractions` (Phase 2116). It
is the sanctioned route from linear to DAG, and it is one-way.

- `DagLift.ofLinearVerified records` re-walks the linear segment (`Verify.segment`) and refuses a broken
  link, a hash that does not recompute, a gap, an empty segment, or one spanning two streams. It then
  lifts the segment to a branch rooted at a DAG genesis.
- `DagLift.ofLinearAnchored anchor records` does the same, with the first record hung off an existing DAG
  node. This is the guest-fork form: a guest's linear journal continued from the host's `Mount` op.
- Both return a `LiftReceipt` naming the linear range and the DAG head it became.
  `LiftReceipt.verify receipt records` recomputes it. What a receipt does and does not prove is in
  [`../CRYPTO.md`](../CRYPTO.md#lift-receipts--a-recomputation-not-a-signature).

The lifted DAG addresses are **not** the linear chain hashes. The linear pre-image folds the sequence and
the previous hash; the DAG pre-image folds the parents. Nothing is re-addressed by a lift: the linear
records are read, never written, and the DAG records are addressed exactly as any DAG record is.
`DagOpRecord.ofLinear` remains as the unverified embedding, for callers that have already verified the
segment and want no receipt.

## Where the UI's DAG stands relative to `Fuaran.Core.OpStream.Dag`

Today `Fuaran.UI.OpStream.Dag` does *not* build on Core's op-DAG: it mints its own content address,
walks its own topology, verifies its own records, and merges with its own tree-level 3-way engine.
That separation was a considered verdict (the "F15" verdict, Phase 408), and the four reasons it gave
were re-examined against Core on 2026-10-16 (Phase 2289). The honest account is now this:

1. **Merge-outcome identity.** A UI merge node's content address folds the **outcome tree's hash**
   under a `"merge"` tag, not the path that reached it. Core's node id is a hash over the sorted
   parents, the actor and the *encoded op*, generic over the op, so the same identity is expressible
   on Core as an op carrying the outcome hash and the record's provenance. This was never a reason to
   stay apart.
2. **Order-independent merge identity.** Both DAGs sort a merge node's parents before hashing, so
   `merge(A, B)` and `merge(B, A)` are one node in either. An earlier version of this document said
   Core joined parents in author order; that was wrong. Core goes further than the UI did: two
   arrivals of an equal node (parents compared as a set, same actor, same op) are deduplicated rather
   than refused, which is the rule Phase 2289 adopts here.
3. **Retention.** Core has a position-sealed checkpoint with compaction, and names the nodes no
   retained root needs (`prunable`). What Core has no notion of is an in-graph **tombstone**, a node
   whose payload is withheld while its address stays; Core's verifier recomputes every node's id. A
   pruned branch's skeleton is therefore a host-side record beside the verified live set, not a node
   in it.
4. **The merge model — the one genuine difference, and the one being retired.** A UI merge node
   *asserts* an outcome and carries a replay delta from its primary parent; replay follows one spine,
   and nothing on the read path checks the assertion against the secondary parent. Core *derives*
   state by folding a head's whole ancestor closure in a deterministic order, and refuses deltas that
   interfere. Measured over the merge-conformance corpus and 18,597 generated triads, Core's fold
   derives none of the UI merge's refusals and, once style edits carry the field they change and the
   footprints are precise, 94.5% of its auto-merges; the remainder is an agreement the reconciler can
   drop, a child-order convention, or a resolution the UI should not have made (a move winning over a
   concurrent removal that the same engine refuses against an edit).

**Direction.** The UI DAG converges onto Core's. First, with no stored byte moving, the UI adopts
Core's node-identity rule for order-swapped merges (Phase 2289). Then, at an announced breaking cut,
the UI DAG becomes Core's DAG over a UI op envelope: Core's `nodeId` is the address, Core's fold is
the replay, Core's checkpoints and `prunable` are the retention, and the tree-level 3-way merge is
removed in favour of Core's conflict report consumed by a thin UI reconciliation policy (a footprint
projection, one rule for concurrent inserts under one parent, the validator gate on the derived
tree). Until that cut ships, the two DAGs remain two implementations, and this document describes
the current one.

**The policy layer ships first** (Phase 2293). `DagReconcile` in `Fuaran.UI.OpStream.Dag.Merge` is
that reconciliation policy, over Core's `Dag.conflictsIn` and the tier's base-aware footprint: an
agreement is applied as no change, two appends of distinct nodes under one parent are ordered by one
declared child-order rule (NodeId ordinal), every other reported pair refuses (a removal against a
concurrent move or edit of the node as `DeleteModify`), and the fold is run in both orders so a pair
outside Core's independence theorem cannot slip through. Its normative statement is
`WIRE_FORMAT.md` §34 and its corpus the `foldFixtures` family, so every host can port it before the
cut makes it the DAG's only merge. Until then it is an additive second entry: `DagMerge` still merges
with the tree-level engine.

**What the policy derives, measured (Phase 2670).** The cut's gate is the derivability snapshot's
`derivability.generated.policy.derived_share` (the share of the tree merge's generated auto-merges the
policy derives to the same tree) at 0.90 or above, with no triad derived to a different tree unless
pinned. Phase 2670 narrowed two base-aware footprints: a reorder reads and writes its parent at the
`kind` slot and reads each listed child only at its `parent` slot, and a move reads and writes its
target at the target's `parent` slot rather than whole. It also widened the snapshot's generator to
draw the pairs those levers and 2651's target (seed 20261016 and 20,000 trials unchanged; the draws are
recorded in `DerivabilitySnapshot.fs`), so the generated population has two editions:

| population | footprint | UI auto-merges | derived to the same tree | `derived_share` | different tree |
|---|---|---|---|---|---|
| 2292's draws (18,597 triads) | before 2670 | 9,294 | 5,943 | 0.6394 | 135 |
| 2292's draws (18,597 triads) | after 2670 | 9,294 | 8,304 | 0.8935 | 180 |
| 2670's draws (17,759 triads) | before 2670 | 10,890 | 5,956 | 0.5469 | 84 |
| 2670's draws (17,759 triads) | after 2670 | 10,890 | 7,936 | **0.7287** | 100 |

No triad the policy derived before the change is refused after it, in either population; every pair
the change frees is pinned in `TreeOpProjectionTests`, and both narrowings are certified by
`Conformance.footprintInLawsAt` (`ReorderMoveFootprintLawTests`). The corpus population stays at
1.0000.

The gate is **not met** on the population that draws the levers' pairs. The 2,851 refused auto-merges
that remain, attributed by the pairs each refusal names (a triad counts under a class when every one of
its refusals is in it):

- **1,240 — a restyle of a container beside a structural write of its child list** (an insert into it,
  a move into or out of it, a removal of its child). Core counts a named structure write as a
  whole-node touch against a slot write, so the parent's `style.<field>` collides with the insert's
  parent. The next lever is Core's: a structure write that is the node's child-list slot for the slot
  rule (2651's finding F2-B). Cleared alone: 0.8426.
- **766 — a reorder beside a concurrent change of the same child list.** The tree merge rebases the
  reorder over the other branch's appends; the policy refuses, because the reorder's list is stale in
  one order. The next lever is the policy's: a declared rule rebasing a reorder over concurrent
  appends, as the interleave rule orders two appends. With the restyle lever: 0.9213.
- **390 — a removal against a touch of the removed node** (`DeleteModify`), refused by design.
- the rest — two moves of one node, two inserts of one id, one slot written twice, and mixtures;
  and 3 fold faults, where both branches move the SAME node into one parent beside other appends
  there: the child-order rule lists the agreed node once per branch, and the `ReorderChildren` it
  records does not apply (a defect of the rule, recorded for the cut).

The **100 different-tree triads** (180 on 2292's draws) are child orders, in two classes: 85 (159) are
a **move to the node's own parent** — the policy's child-order rule takes it as an append and moves
the node to the end of the run, where the tree merge keeps its base slot; and 15 (21) are an **append
order** — the tree merge sorts every appended id by NodeId, where the policy keeps each branch's run in
its own order and merges the runs by their heads (6 of them involve no move at all). Pinning each
class as a fixture, or aligning one rule to the other, is a ruling the cut owes before it lands.

What *did* transfer regardless: the provenance-hardening applied to the linear chain (folding the
prompt id / actor / result outcome into the content hash, and delimiting the pre-image) was applied to
the UI DAG's node hash too – so a DAG node's digest covers exactly what a linear record's does.
(What that digest is worth is the same in both cases: it detects corruption and reordering, and it is
unkeyed, so it is not evidence against a writer who re-chains – see [`../CRYPTO.md`](../CRYPTO.md).)

## In short

- One stream, one writer → **linear chain**.
- A linear stream that must continue as a branch → **lift it** with `DagLift`, never re-read it.
- Diverging writers that must merge → **branching DAG**.
- The UI DAG is converging onto the Core DAG by decision (identity rule now, the merge model at the
  announced breaking cut). Until that cut, treat the two as two implementations and do not unify them
  piecemeal – the migration is one recorded act with a re-mint, not a series of edits.
