# Structure-only clean room

`Fuaran.UI.Ops.CleanRoom` lets an **untrusted party** (a model provider, a cloud function, a
counterparty) reorganise the *structure* of a `Node<'Msg>` tree **without ever receiving its
content**. A content-free *skeleton* of the tree crosses a clean-room divide; the untrusted side
emits structural ops that reference nodes **by id**; the trusted side replays those ops against the
real tree locally. The privileged prose never serialises across the wire.

This is unusually clean to build on Fuaran because the canonical wire artefact is a `TreeOp` stream
over **id-referenced** nodes (see [`WIRE_FORMAT.md`](./WIRE_FORMAT.md)): a reorganisation is "move
node `X` under `Y` at index 2" / "reorder the children of `Z`" / "delete subtree `W`" – every op
references nodes *by stable `NodeId`*, and the actual content lives in the leaf nodes. The
content/structure split this needs is therefore **native to the wire format**, not bolted on: a
content-free skeleton is a lossy projection of the same tree the ops are authored against.

## The mechanism – two pure functions + a substitutable gate

```
                      ┌──────────────── trusted perimeter ────────────────┐
   real Node tree ──▶ project ──▶ Skeleton ─┐                             │
   (content + ids)                           │  (content-free)            │
                                             ▼                            │
                              ╌╌╌╌╌╌╌╌╌ clean-room divide ╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌┘
                                             │  skeleton crosses; the untrusted side
                                             ▼  authors id-referenced structural ops
                                       inbound TreeOp
                                             │
   real Node tree ◀── Apply.apply ◀── StructuralOpBroker.Enforce  (Released | Withheld reason)
```

1. **Skeleton projection** – `Skeleton.project : Node<'Msg> -> Skeleton`. Walks the real tree and
   emits a content-free shadow: per node its `NodeId`, its structural `Kind` discriminator (a
   fixed-vocabulary type name such as `"Heading"` / `"Dashboard"`, *never* the node's text), and a
   bounded `StructuralDescriptor` (a coarse structural role + coarsened child-count / content-length
   buckets). **The `Skeleton` type has no content field** – the projection cannot leak prose because
   there is nowhere for prose to go.

2. **Structural-op gate** – `StructuralOpBroker` (an `IStructuralOpBroker`). `Enforce` validates an
   inbound `TreeOp` against the issued skeleton and returns `Released op` / `Withheld reason`. It
   withholds any op that
   - references a `NodeId` not present in the issued skeleton (no smuggling in fabricated targets),
   - sets, mutates, carries, or inserts **content** (a structural op moves / reorders / reparents /
     deletes by id; it never authors text), or
   - falls outside the structural allowlist (move / reorder / reparent / delete).

   The gate is a pure, stateless function of `(skeleton, op)`. Everything that survives is, by
   construction, a content-free id-referenced rearrangement. Applying it on the trusted side
   re-derives the real document – see *Determinism* below.

This is the same **substitutable clean-room broker seam** an aggregate (k-anonymity) clean-room gate
uses – the same divide and the same `Released` / `Withheld reason` decision shape, but a *different
enforcement predicate*: a structural-op floor in place of an aggregate-output floor. A deployment
that needs a different structural floor substitutes its own `IStructuralOpBroker` without changing
call sites. (The aggregate broker's `Released` carries an aggregate result, which has no meaning for
a tree op, so this package mirrors the broker's *shape* over the `TreeOp` payload rather than reusing
that type literally.)

## Structure leaks content – abstraction, not redaction

The load-bearing discipline: **structure is often content.** A section heading is frequently itself
sensitive, and tree shape, child ordering, node counts, and even text *lengths* are side-channels.
So "structure without content" cannot be naive **redaction** (blank the text) – it must be
**abstraction**: replace specifics with bounded type tags and coarsened magnitudes.

This package ships the **mechanism** of that abstraction:

- The `StructuralDescriptor` is a **bounded** type – a domain-neutral `StructuralRole` (derived from
  the node's kind alone) plus coarsened `CountBucket` / `LengthBucket` magnitudes. It carries no
  free-form content string by construction.
- Child counts and content lengths are **coarsened** into a small set of buckets (e.g. 6 and 10
  children both project to the same bucket; 200 and 900 characters both project to the same bucket),
  the same instinct as an aggregate gate's cell-suppression floor – exact magnitudes never leave the
  perimeter.

A consuming domain layers a **richer (still bounded) descriptor vocabulary** on top via the
`projectWith` / `issueWith` classifier seam (`Classify<'Msg> = Node<'Msg> -> StructuralDescriptor`).
The classifier maps a node to a bounded descriptor; it cannot widen the type into a content-carrying
one. The domain-specific tag vocabulary + the classifier that decides *which* descriptor a node gets
are **out of scope here** – this package is the mechanism only.

## Determinism – the ops re-derive the real document

Because a surviving op is a content-free, id-grounded rearrangement, applying it to the real tree is
**content-position-independent**: the result is byte-identical (under the canonical-JSON encoder, see
[`WIRE_FORMAT.md`](./WIRE_FORMAT.md)) to taking a structurally-identical content-free stand-in,
applying the same op, and re-attaching the real content by `NodeId`. The test suite proves this for
reorder / move / delete / batch ops via the canonical-JSON oracle: *"I sent a content-free skeleton
and got back only id-referenced structural moves"* is guaranteed by the gate, not by trusting the
counterparty.

## Auditability

Every issued skeleton and every gate decision is audit-emitted (`ICleanRoomAuditSink`), so the divide
is traceable end to end. The audit records are themselves **content-free**: a skeleton issuance
records the root id + a node *count*; a gate decision records the op's structural kind, the ids it
referenced, and the Released / Withheld outcome. The broker stays pure – audit emission is a separate
seam (`issue` / `enforceAudited`) a host wires to its op-stream / telemetry sink. A host adapts
`ICleanRoomAuditSink` to a durable sink for a persistent, reviewable trail.

## Threat surface + assumptions

- **Node ids are opaque tokens.** Ids cross the divide by design (the counterparty needs them to
  author id-referenced ops). The mechanism assumes ids are opaque structural handles, not prose – 
  an author who embeds sensitive specifics *in an id string* defeats the projection. Hosts should
  mint opaque ids for cross-divide use.
- **Counts / lengths / ordering are side-channels** and are coarsened, not eliminated. The bucket
  granularity is a deliberate floor; a deployment with a sharper threat model tightens it (or maps
  more kinds to `Empty` / `None`).
- **String→DOM safety on replay** is the renderer's existing contract – see
  [`SANITIZATION.md`](../SANITIZATION.md). Brokered ops are content-free, so they introduce no new
  string→DOM seam; the real content they rearrange is rendered through the same sanitised path as
  any other tree.
- **Privacy-by-construction or don't build.** If a projection cannot be shown content-free under
  audit for a given threat model, the cross-divide mode does not ship for it – fully in-perimeter
  restructuring is unaffected.

## Non-goals (out of scope for this package)

- **Content editing.** Tightening, rewriting, or otherwise *authoring* content needs the content,
  which by definition is not a structure-only operation. Content-bearing editing tiers are an
  explicit, separate, operator-gated escalation layered by consuming applications – never a silent
  widening of this floor.
- **A domain tag vocabulary / classifier.** The richer descriptor vocabulary that names a node's
  domain role is the consuming domain's, layered via the classifier seam above.

## Surface summary

| Surface | Role |
|---|---|
| `Skeleton.project` / `projectWith` | Content-free projection (default / domain-classifier). |
| `Skeleton.Skeleton` / `SkeletonNode` / `StructuralDescriptor` | The content-free shadow types (no content field). |
| `Skeleton.nodeIds` / `knownIds` / `nodeCount` | Skeleton queries (the broker's id-membership index). |
| `Broker.StructuralOpBroker` / `IStructuralOpBroker` | The substitutable structural-op gate. |
| `Broker.StructuralGateDecision` | `Released op` / `Withheld reason`. |
| `Audit.ICleanRoomAuditSink` + `issue` / `enforceAudited` | Content-free audit emission (pure-gate-preserving). |
