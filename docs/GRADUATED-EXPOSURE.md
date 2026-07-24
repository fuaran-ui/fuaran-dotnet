# Graduated exposure – choose how much your AI sees, per document

The [structure-only clean room](./STRUCTURE-ONLY-CLEAN-ROOM.md) lets an AI reorganise a document's
*structure* without ever receiving its *content*. That zero-content mode is the **privacy floor** of
a graduated model, not the ceiling. An application built on the floor lets an operator choose, **per
document**, how much the model sees – and every step above the floor is an explicit, operator-made
choice, never an automatic one.

The headline: *AI document help where you decide the exposure for each document – and the floor is
"the model sees no content at all", guaranteed by construction.*

## The three tiers

| Tier | What the model sees | What it can do | Exposure |
|---|---|---|---|
| **Structure-only** *(default)* | a content-free abstracted skeleton – ids, kinds, bounded structural metadata | reorder / regroup / reparent / dedupe / gap-detect against a template | **zero content** |
| **Classified-summary** | per-node abstracted summaries the operator approves, node by node | the above + structure-aware suggestions referencing clause *kinds* | operator-gated, per node |
| **Full BYOK** | the real content, under the operator's own key | full editing, including rewrites | operator accepts exposure |

Each tier exposes **strictly more** than the tier below it. Structure-only is the **default**: a
document with no explicit choice sits at the zero-content floor.

## Exposure guarantees per tier

- **Structure-only – zero content, by construction.** The model receives only the content-free
  skeleton: stable node ids, fixed-vocabulary structural kinds, and *bounded* structural descriptors
  (a coarse structural role + coarsened child-count / content-length buckets). The skeleton type has
  no content field, so there is nowhere for prose to go. The model emits structural ops that
  reference nodes **by id**; a substitutable gate releases only content-free, id-grounded
  rearrangements, and the trusted side replays them against the real document locally. The mechanism,
  the gate, the determinism proof, and the auditability are all the
  [structure-only clean room](./STRUCTURE-ONLY-CLEAN-ROOM.md)'s – this tier *is* that floor.

- **Classified-summary – per-node abstractions the operator approves.** Above the floor, the model
  may see bounded per-node *summaries* – abstractions, never raw prose – but only ones the operator
  has reviewed and approved, node by node. The exposure is whatever the operator explicitly releases
  and no more. This tier depends on the in-perimeter classifier posture below.

- **Full BYOK – the operator accepts exposure.** The widest tier sends the real content under the
  operator's own provider key. There is no clean-room divide here: it is ordinary bring-your-own-key
  editing, chosen explicitly when the operator decides the value of a content-level edit (tightening
  a clause, rewriting for plain language) is worth the exposure.

## The in-perimeter classifier – and why it is operator-reviewable

Building a *safe* skeleton above the bare floor needs a judgement: **structure is often content.** A
section's heading can itself be sensitive, and tree shape, child ordering, counts, and even text
lengths are side-channels. So "structure without content" is **abstraction, not redaction** – replace
specifics with bounded type tags and coarsened magnitudes (see
[the structure-only clean room](./STRUCTURE-ONLY-CLEAN-ROOM.md)). Deciding *which* bounded tag a node
gets is a classification that may itself want model help – which is mildly circular: you can need a
trusted pass to build the very skeleton that protects the document.

The honest resolution, and the load-bearing privacy posture:

- **The classifier runs inside the perimeter.** It maps real nodes to the bounded structural
  descriptors *before* projection, on a local / self-hosted model, never a third party. It plugs into
  the public classifier seam (`Classify` / `projectWith` / `issueWith`) the clean room already
  exposes – it cannot widen the descriptor into a content-carrying type.
- **Its output is operator-reviewable before anything crosses the divide.** The operator sees, and
  can override, the abstracted skeleton the classifier produced *before* it is issued. The
  cross-divide model only ever sees the post-classification, post-review skeleton – never the raw
  tree.

This is the discipline that keeps the higher tiers honest: even where a model assists, no content
crosses the divide that an operator did not first see abstracted and approve.

## Escalation is explicit – never automatic

Two rules make a graduated model a deliberate choice rather than a slippery slope:

1. **Structure-only is the default.** No explicit choice means the zero-content floor.
2. **Every step above the floor is an explicit operator opt-in** – surfaced, acknowledged, and
   recorded. Nothing in the system widens a document's exposure on its own; there is no path from a
   narrower tier to a wider one that the operator did not take by hand.

So the robustness risk of the abstraction problem is **de-risked by design**: even if classification
were imperfect, structure-only stays a safe, shippable floor, and the wider tiers are explicit
opt-ins layered on top – not a softening of the floor's guarantee.

## Why it sells – a compliance wedge for regulated document work

- **Hard bars on sending content to a third-party model exist** in regulated work – legal
  (privilege + confidentiality), healthcare (PHI), finance (data-room walls), government. "AI
  document help that is privileged-by-construction" clears a procurement gate that generic AI tools
  cannot.
- **It is a mechanical guarantee plus an audit trail, not a policy promise.** A security reviewer
  gets the gate (content-free ops by construction) and a content-free audit record of every skeleton
  issued and every op released or withheld – the same instrument a buyer's diligence wants, pointed
  at a sharp objection.
- **The graduation is itself the sales story** – *choose how much your AI sees, per document* – and
  the floor means the operator can adopt the zero-content mode first and escalate only where, and
  when, they decide it is worth it.

## Privacy-by-construction or don't build

If a skeleton cannot be shown demonstrably content-free under audit for a given threat model, the
cross-divide mode does not ship for it – only fully in-perimeter editing (the operator's own key,
inside the perimeter) does. The floor's guarantee is not negotiable against the value of the wider
tiers.

## See also

- [Structure-only clean room](./STRUCTURE-ONLY-CLEAN-ROOM.md) – the mechanism the floor is built on
  (skeleton projection, the substitutable structural-op gate, the determinism proof, auditability,
  the abstraction-not-redaction discipline, and the classifier seam).
- [Wire format](./WIRE_FORMAT.md) – the id-referenced `TreeOp` stream that makes the content/structure
  split native rather than bolted on.
- [Render-time sanitization](../SANITIZATION.md) – the string→DOM safety contract the real content is
  rendered through on replay.
