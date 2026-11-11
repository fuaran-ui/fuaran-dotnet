# Op-stream shapes: linear chain vs. branching DAG

Fuaran ships **two** op-stream persistence shapes: a linear chain and a branching DAG. Since Phase 2645
(the cut) the DAG **is** `Fuaran.Core`'s DAG, over the linear chain's own provenance entry. This document
says which shape to use, how the cut was made, and how it was gated.

## Which one do I use?

| You are building… | Use | Package(s) |
|---|---|---|
| A single-writer, sequenced, audit-logged stream (server-driven UI, deterministic replay, one authoritative timeline) | **Linear chain** | `Fuaran.UI.OpStream.Abstractions` + `.InMemory` / `.Sqlite` / `.Replay` |
| A multi-agent / branching / eventually-consistent history (concurrent editors, fork-and-merge, offline-then-reconcile) | **Branching DAG** | `Fuaran.UI.OpStream.Dag.*` |

**Default to the linear chain.** It is simpler, cheaper, and covers every single-timeline case. Reach
for the DAG only when two writers genuinely diverge and must be merged – the DAG exists to make
"two hosts that reached the same tree agree" a first-class, verifiable property.

Both shapes share the same op vocabulary (`TreeOp`), the same provenance entry (`StreamEntry`: the op,
the timestamp, the prompt and the result), the same canonical encoder, and the same host-side SHA-256
`HashFn`. Moving from linear to DAG is a persistence-layer choice, not a rewrite of your edit logic.

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
the previous hash; the DAG pre-image (Core's `Dag.nodeId`) folds the parents. The two share the entry byte
for byte, so a lifted node's op IS the linear record's op. Nothing is re-addressed by a lift: the linear
records are read, never written, and the DAG records are addressed exactly as any DAG record is.
`DagOpRecord.ofLinear` remains as the unverified embedding, for callers that have already verified the
segment and want no receipt.

## The UI's DAG is `Fuaran.Core.OpStream.Dag` (Phase 2645, the cut)

A node is a Core `DagNode<StreamEntry<'Msg>>`. Its address is Core's `Dag.nodeId` under the tier's SHA-256,
over the sorted parents, the actor and the entry (`WIRE_FORMAT.md` §5.3.3). Verification is Core's
`Dag.firstBreak` (and, on the sinks' read paths, the same address recomputed with parents resolved
store-wide). Topology is Core's (`Dag.ancestorsOf`, `Dag.mergeBase`), retention is Core's `Dag.prunable`,
and replay, checkpoints and compaction are Core's (`Dag.tryReplayTo`, `Dag.Checkpoint`, `Dag.replayFrom`,
`Dag.compactAt`), bound to the tier by `DagFold` in `Fuaran.UI.OpStream.Dag.Merge`.

**The merge is the reconciliation policy.** `DagReconcile` (`WIRE_FORMAT.md` §34) consumes Core's conflict
report: an agreement applies as no change, two appends under one parent are ordered by one declared
child-order rule, a reorder beside a change of its list is rebased, and every other reported pair refuses —
a removal against a concurrent touch of the removed node as `DeleteModify`. The fold runs in both orders and
a disagreement refuses. `DagMerge.mergeDag` is the one merge: it derives with the policy, gates with the
validator, and has Core mint the merge node — an ordinary node with the two heads as parents whose op is the
script. Core's replay of that node folds both branches in Core's drain order and then the script, so before
the node is handed back its replay is checked against the derived tree; and the script carries the child-
order rule's reorder for every interleaved or rebased parent, whether or not the order a reconcile checked
already matched (the reorder, not the drain order, fixes the children). A caller may name a precedence side:
its refused ops are re-asserted as script, which settles two writes of one slot and nothing that one fold
order cannot apply.

**What the cut retired.** The tree-level three-way merge (`TreeMerge`), its per-cell primacy
(`DagPrimacy`, `ReassertPin`) and its conflict envelope (`MergeConflict`); the merge-outcome node identity
and the third hash function; the primary-spine replay (`DagReplay`) and its own checkpoint seal
(`DagCheckpoint`); the topology module (`DagTopology`, the virtual base); the in-record tombstone; and the
merge-conformance corpus's triad families. A pruned node now leaves the live set for the sink's skeleton
(address and parents, no payload). Every stored address moved once; the re-mint tool is fuaran#2648's.

**The tree merge's F15 reasons, re-examined (Phase 2289, kept as the record of why this was safe).**
Merge-outcome identity was expressible on Core as an op; Core already sorted parents and deduplicated equal
nodes; Core already had checkpoints, compaction and `prunable`; and the one genuine difference — the UI
merge node asserted an outcome and replayed a delta along one spine, where Core derives state by folding
the head's closure — was measured, and is what the gate below was for.

### How the cut was gated (history)

**The policy layer shipped first** (Phase 2293), as an additive second entry beside the tree merge, with
the `foldFixtures` corpus so every host could port it before the cut made it the DAG's only merge. The cut
was held behind the operator's D2-B ruling: dispatched only when the derivability snapshot reported the
policy deriving at least 90% of the tree merge's generated auto-merges to the same tree, with no triad
derived to a different tree unless pinned. The `derivability-snapshot` benchmark that measured it compared
against the tree merge, and was retired with it at the cut; the figures below are its last.

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
1.0000. The change also widens what the policy derives where the tree merge REFUSES (2292's draws:
142 → 606 triads; 2670's: 158 → 441), each a pair whose ops the fold runs in both orders to one tree.
One is a corpus fixture: `merge-totality-concurrent-move` (a move beside a restyle of the moved node),
which the tree merge refuses because it cannot keep both the mover's position and the editor's
subtree, and the fold now derives with both. The cut changes that fixture's verdict.

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

#### The reorder rebase and the pinned classes, measured (Phase 2671)

The operator ruled on 2026-11-10: pin both child-order classes to the policy's rule, accept the triads the fold
derives where the tree merge refuses, and keep the 0.90 gate with no fallback to the tree merge. Phase 2671 added a
policy class, the **rebase**: a reorder of a parent beside an append to it, or a removal or move of a child it lists,
is admitted, and the fold applies the reorder rebased to the children the parent then holds (WIRE_FORMAT §34.3–34.4).
It also lists a node both branches move into one parent once, which removes the 3 fold faults (5 on 2292's draws).

**A premise corrected on the way.** The 766 refusals were recorded as pairs "the tree merge rebases". It does not:
the tree merge refuses any reorder beside a structural change of the same list (`ReorderVsStructural`), and
auto-merges only where the reorder leaves the list as the base had it — chiefly a reorder of `sub`, which has one
child in the base and so usually shuffles to the same order (of the 1,158 refused auto-merges naming a reorder on
2670's draws before this phase, 859 reorder only `sub`). The rebase frees those, and also derives the non-identity reorders the tree merge
refuses, which the ruling accepts.

| population | policy | UI auto-merges | same tree | `derived_share` | pinned own-parent move | pinned append order | unpinned different tree |
|---|---|---|---|---|---|---|---|
| 2292's draws | after 2670 | 9,294 | 8,304 | 0.8935 | 159 | 21 | 0 |
| 2292's draws | after 2671 | 9,294 | 8,452 | **0.9094** | 242 | 24 | **0** |
| 2670's draws | after 2670 | 10,890 | 7,936 | 0.7287 | 85 | 15 | 0 |
| 2670's draws | after 2671 | 10,890 | 8,758 | **0.8042** | 147 | 31 | **0** |

The corpus population stays at 1.0000. The 2670's-draws rows are `derivability-snapshot`'s generated population
(seed 20261016, 20,000 trials), which now prints the two pinned cells beside `derived_elsewhere`; the 2292's-draws
rows come from a probe running 2292's generator verbatim against the same libraries. Triad by triad, no triad the
policy derived before is refused or derived elsewhere after; the moves are 820 refused and 2 faulted → same tree
(2292's: 145 and 3), 77 refused and 1 faulted → a pinned order (84 and 2), and 1,112 refused and 9 faulted → derived
where the tree merge refuses (2,131 and 14). No fault, order-sensitive refusal or unpinned different tree remains. Every
different-tree triad differs from the tree merge in child order alone and falls in a pinned class.

**The gate.** On 2292's draws it is **met** (0.9094, no unpinned different tree). On 2670's draws it is **not met**
at the Core 0.41.0 pin: 0.8042. Of the 1,954 refused auto-merges left (786 `DeleteModify`, 1,168 `Conflict`), 1,346 are the restyle of a container beside a
structural write of its child list (Core counts a named structure write as a whole-node touch against a slot write),
which fuaran-core#625 (a structure write is the node's child-list slot) targets; with every one of them cleared the
share is **0.9278**, an attribution bound from this phase's probe, not a measurement with 625 pinned. 625 is
unreleased, so **the gate figure with 625 pinned is owed after its release** — it is fuaran#2645's dispatch check. The
rest: 259 a removal against a touch of the removed node (DeleteModify, by design), 52 two moves of one node or into
one parent, 47 two inserts of one id, 70 an insert beside a move in one parent, 54 two reorders of one parent that
differ — mostly a reorder that leaves its list unchanged beside a real one, which the tree merge treats as no change
(the next policy lever, if one is wanted: a reorder to the order the parent already holds is no write), and mixtures.

**What the fold derives where the tree merge refuses** (1,562 triads on 2670's draws, 2,751 on 2292's), by the tree
merge's refusal class, each pinned by one `foldFixtures` fixture:

- `ReorderVsStructural` with a reorder in it (1,105; 2,131): a reorder beside a change of the same child list —
  `fold-reorder-and-insert`.
- `ReorderVsStructural` with no reorder (180; 197): a move to a node's own parent beside an append there (176; 191) —
  `fold-own-parent-move-and-insert`; and an agreed append beside other appends (4; 6) —
  `fold-double-move-and-appends`.
- `ConcurrentMove` (273; 415, plus 14; 85 mixed with the above): a move beside an edit of the moved node —
  `fold-move-and-target-edit`, the pair `merge-totality-concurrent-move` refuses.
- `ConcurrentEdit` on `insert` (4; 8, plus 0; 2 mixed): both sides move a node to one parent and one edits it — the
  tree merge reads it as inserted twice with different content — `fold-agreed-move-and-edit`.

#### The Core 0.42.0 pin and the child-list slot, measured (Phase 2691)

Core 0.42.0 (fuaran-core#625, Core D246) compares a structure write of `p` (an insert into it, a move into or out of
it, a removal under it) against a slot access as a read and a write of `(p, Footprint.childListSlot)`, and a slot
access meets the node sets only at a content write. Phase 2691 raises the pin and, in the same commit, makes every
arm of `TreeOpFootprint.ofOpIn` that reads or rewrites a child list declare that slot: a resolved reorder, a resolved
move's source parent and `EditNode` (and, as a guard, any in-place edit whose held set moved). A resolved move also
reads its destination's own `parent` slot, because the cycle check reads the whole chain and the destination's
existence read no longer collides with a move of it. Without those declarations the 0.42.0 fold derives pairs that do
not compose: run with the 2671 arms against Core 0.42.0, the probe below faults the fold on 572 triads of 2670's draws
(25 of 2292's), which is the hole the slot closes.

| population | policy | UI auto-merges | same tree | `derived_share` | pinned own-parent move | pinned append order | unpinned different tree |
|---|---|---|---|---|---|---|---|
| 2292's draws | Core 0.41.0, after 2671 | 9,294 | 8,452 | 0.9094 | 242 | 24 | 0 |
| 2292's draws | **Core 0.42.0, after 2691** | 9,294 | 8,452 | **0.9094** | 242 | 24 | **0** |
| 2670's draws | Core 0.41.0, after 2671 | 10,890 | 8,758 | 0.8042 | 147 | 31 | 0 |
| 2670's draws | **Core 0.42.0, after 2691** | 10,890 | 10,155 | **0.9325** | 183 | 37 | **0** |

The 2670's-draws rows are `derivability-snapshot`'s generated population (seed 20261016, 20,000 trials, the
benchmark run on the pinned tree; the corpus population stays at 1.0000); the 2292's-draws rows are the per-triad
probe 2670 and 2671 used, against the same libraries. Triad by triad on 2670's draws: 1,397 refused auto-merges now
derive the same tree, 36 and 6 now derive in the pinned own-parent-move and append-order classes, and 233 triads the
tree merge refuses now derive; nothing the policy derived before is refused or derived differently, and 2292's draws
do not move (their generator draws no restyle of a container). No fold fault and no order-sensitive refusal remains,
and no triad the fold derives with no conflict reported reaches different trees in its two sequential orders.

**The gate.** fuaran#2645's gate (D2-B, F4-A: `derived_share` at or above 0.90 on the widened population with no
unpinned different-tree triad) is **met** on the Core 0.42.0 pin: 0.9325 on 2670's draws and 0.9094 on 2292's, with
no unpinned different tree on either. The campaign driver re-ran the snapshot from the primary clone at the landed
tree (b052c482, the same seed and trials): 0.9325, no triad derived elsewhere, 183 and 37 in the two pinned classes.
The cut was dispatched on that figure.

What remains refused on 2670's draws (515 auto-merges of 10,890: 308 `DeleteModify`, 207 `Conflict`), by the kinds
of the op pairs refused: a move beside a removal (234, `DeleteModify`, by design), two moves of one node or into one
parent (65), two inserts of one id (63), two reorders of one parent that differ (55, mostly a reorder that leaves its
list as it was beside a real one: the policy lever named in the 2671 section, a reorder to the order the parent
already holds being no write), a reorder beside a removal or move it cannot rebase (16), an insert beside a move in one
parent (9), an edit or restyle beside a removal of its node (14, `DeleteModify`), two edits or retones of one slot
(3), two other pairs, and 54 mixtures of these.

The provenance-hardening applied to the linear chain (folding the prompt id, actor and result outcome into
the content hash, and delimiting the pre-image) holds for a DAG node by construction since the cut: its op
IS the chain's entry. What that digest is worth is the same in both cases: it detects corruption and
reordering, and it is unkeyed, so it is not evidence against a writer who re-chains – see
[`../CRYPTO.md`](../CRYPTO.md).

## In short

- One stream, one writer → **linear chain**.
- A linear stream that must continue as a branch → **lift it** with `DagLift`, never re-read it.
- Diverging writers that must merge → **branching DAG**.
- The UI DAG IS the Core DAG (Phase 2645): Core's node, address, verifier, topology, retention, replay
  and checkpoints, with the reconciliation policy as its only merge. A store written before the cut is
  re-minted once (fuaran#2648), never dual-read.
