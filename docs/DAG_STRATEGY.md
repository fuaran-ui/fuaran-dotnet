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

## Why is the UI's DAG separate from `Fuaran.Core.OpStream.Dag`?

`Fuaran.Core` ships a **minimal** op-DAG whose node identity is essentially
`hash(parents-joined , actor+op)`. `Fuaran.UI.OpStream.Dag` does *not* build on it, and that is a
considered verdict – the two DAGs share the `TreeOp` / stream-witness vocabulary but own genuinely
different *temporal-graph identity*. The UI DAG needs four things the Core DAG deliberately does not
model:

1. **Merge-outcome identity (the load-bearing property).** A UI merge node's content address folds
   the **outcome tree's hash** under a `"merge"` tag: *two hosts agree iff they reach the same tree*.
   Core's node hash has no concept of a merge outcome.
2. **Order-independent merge identity.** The UI DAG sorts a merge node's parents lexicographically, so
   `merge(A, B)` and `merge(B, A)` are the same node. Core joins parents in author order.
3. **Retention + tombstones.** The UI DAG models pruning history (`DagRetention`, tombstones) for
   long-lived collaborative streams. Core models neither.
4. **A tree-aware 3-way merge engine.** The UI DAG carries the merge machinery (`.Dag.Merge`); Core's
   DAG is pure graph topology + a minimal node hash.

Growing the Core DAG to fit these would burden every other (non-branching) Core adopter with
UI-specific merge semantics. The right boundary is: **Core owns the generic tree/op/stream spine; the
branching, merging, retention-bearing op-DAG is a UI-domain concern that reuses that spine's
vocabulary but keeps its own identity.** (Internally this is tracked as the "F15" verdict.)

What *did* transfer regardless: the provenance-hardening applied to the linear chain (folding the
prompt id / actor / result outcome into the content hash, and delimiting the pre-image) was applied to
the UI DAG's node hash too – so a DAG node's digest covers exactly what a linear record's does.
(What that digest is worth is the same in both cases: it detects corruption and reordering, and it is
unkeyed, so it is not evidence against a writer who re-chains – see [`../CRYPTO.md`](../CRYPTO.md).)

## In short

- One stream, one writer → **linear chain**.
- A linear stream that must continue as a branch → **lift it** with `DagLift`, never re-read it.
- Diverging writers that must merge → **branching DAG**.
- The UI DAG being separate from the Core DAG is a boundary decision (merge-outcome identity, sorted
  parents, retention, tree-merge), not a half-finished migration – please don't "unify" them without
  re-opening that decision.
