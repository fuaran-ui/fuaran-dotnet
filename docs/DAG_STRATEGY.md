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
