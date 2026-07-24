# Fuaran.UI.Ops.CleanRoom

A **structure-only clean room** for `Fuaran.UI.Ops`: let an untrusted party (a model
provider, a cloud function, a counterparty) reorganise a `Node<'Msg>` tree **without
receiving its content**.

Because the Fuaran wire artefact is a `TreeOp` stream over **id-referenced** nodes — ops
reference `NodeId`s, content lives in leaf nodes — the content/structure split is native:
a content-free *skeleton* can cross a clean-room divide, the untrusted side emits structural
ops by id, and the trusted side replays them against the real tree. The privileged prose
never serialises across the wire.

Three pieces, all pure and Fable-clean:

| Surface | Role |
|---|---|
| `Skeleton.project : Node<'Msg> -> Skeleton` | Content-free shadow of a tree — per node: `NodeId`, structural `Kind`, a bounded `StructuralDescriptor` (role + coarsened child-count / content-length buckets). **No content field exists on the `Skeleton` type** — the projection cannot leak prose. |
| `Broker.StructuralOpBroker` (`IStructuralOpBroker`) | The structure-only gate. `Enforce` validates an inbound `TreeOp` against the issued skeleton and returns `Released op` / `Withheld reason`. Withholds any op that references an unknown `NodeId`, authors / carries content, or falls off the move/reorder/reparent/delete allowlist. Pure, stateless. |
| `Audit` (`ICleanRoomAuditSink`) | Content-free audit of every issued skeleton + every gate decision. `issue` / `enforceAudited` compose projection + enforcement with emission while keeping the gate pure. |

## Shape

```fsharp
open Fuaran.UI.Ops.CleanRoom

// Trusted side — inside the perimeter:
let sink   = Audit.InMemoryCleanRoomAuditSink()
let broker = Broker.StructuralOpBroker.create ()
let skeleton = Audit.issue sink realTree        // project + audit issuance; this crosses the divide

// Untrusted side authors id-referenced structural ops against `skeleton`, e.g.
//   TreeOp.ReorderChildren (parentId, newOrder)

// Trusted side gates every inbound op before replay:
match Audit.enforceAudited sink broker skeleton inboundOp with
| Broker.StructuralGateDecision.Released op -> Apply.apply op realTree   // safe to replay
| Broker.StructuralGateDecision.Withheld reason -> // record + reject
```

A surviving op is, by construction, a content-free id-referenced rearrangement: applying it
to the real tree re-derives the same canonical-JSON document as a content-reattach round-trip
(the determinism proof in the test suite, via `CanonicalJson`).

## Abstraction, not redaction

Structure leaks content — a heading's text is often itself sensitive, and child counts,
ordering, and text *lengths* are side-channels. So the skeleton **abstracts** (a bounded role
tag + coarsened magnitude buckets) rather than **redacts** (blank the text). This package ships
the **mechanism**; the domain-specific tag vocabulary + classifier that decides *which* richer
descriptor a node gets is the consuming domain's, layered on via the `projectWith` /
`issueWith` classifier seam. See [`docs/STRUCTURE-ONLY-CLEAN-ROOM.md`](../../docs/STRUCTURE-ONLY-CLEAN-ROOM.md).

Additive — a simple-tree app references nothing and pays nothing.
