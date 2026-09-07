# 1587 — a host's private `TreeOp` re-typing becomes `TreeOp.mapMsg`

**Packages:** `Fuaran.UI.Ops.Abstractions` (new surface), `Fuaran.UI.OpStream.Abstractions`
(new surface), `Fuaran.UI.OpStream.Dag.Abstractions` (**source-breaking**, two functions).

## What changes, in one paragraph

The decoder returns `TreeOp<obj>`. A host that persists its op-stream as text and reads it back
needs the op at ITS `'Msg`, so it has to supply an `IOpJsonCodec<'Msg>` — and until now the only
codec this tier shipped was `OpJsonCodec.encodeOnly`, whose decode always errors. Every host that
wanted a readable stream therefore wrote the same structural re-typing of the whole op and node
vocabulary into its own tree. That mapping is now `TreeOp.mapMsg`, and
`OpJsonCodec.canonical` composes it with the tier's own encoder and decoder: **a host supplies one
mapper and gets a round-tripping codec.**

## If you carried a private re-typing

Delete it. Replace the codec you built around it with:

```fsharp
open Fuaran.UI.OpStream.Abstractions

// `mapper : obj -> 'Msg option` — the ONE thing that was ever host-specific.
let codec : IOpJsonCodec<MyMsg> = OpJsonCodec.canonical (fun _ -> Some MyMsg.Noop)
```

**Most hosts' mapper is a constant, and that is correct rather than lazy.** `JsonDecode` replaces
every message it cannot carry across the wire with the same `"<closure>"` sentinel — a decoded op
never carries a real message, because the wire has no message type — so a host whose decoded ops are
replayed for their STRUCTURE (which is what a replay is) answers with whatever inert message its
own type offers. A host that does discriminate reads the payload and answers per case; a payload it
cannot type is `None`, which refuses the decode by name.

At the erased `'Msg`, `OpJsonCodec.canonicalObj ()` is the same codec with the identity mapper —
the pair a law suite or a `'Msg`-agnostic tool wants. This repo's DAG and persistence law suites
had each written that pair by hand; both now call it.

## The refusal, and the one thing it does not cover

`TreeOp.mapMsg : (obj -> 'Msg option) -> TreeOp<obj> -> Result<TreeOp<'Msg>, MapRefusal>` is total
over all eleven op cases and **never substitutes a default** for a payload the mapper declines. A
`MapRefusal` names the op case, the op's slot (`"child"`, `"kind"`, `"state"`, `"node"`, and
`"ops[i]."`-prefixed inside a `Batch`), the addressed node, and each distinct payload declined;
`MapRefusal.render` is the one-line form `OpJsonCodec.canonical` reports as its decode error.

A `'Msg` sits in a node in one of two ways and they are not equally reachable, so the contract has
two halves:

- **Eagerly stored** — `Action.Dispatch`'s message is a field. Every such payload is visited before
  anything is constructed at your `'Msg`, so a decline here is an `Error` and nothing is built.
- **Closure-carried** — `Action.Call`'s `onResult`, `ReadFileBody`'s `onRead`, `Tabs.OnSelect`, a
  custom cell's render. Their payloads do not exist until the closure is called, and calling a
  host-supplied function with a fabricated argument to find out is not something a map may do. They
  relabel by composition, and a decline at invocation raises `MapRefusalRaised` carrying the same
  `MapRefusal`.

That asymmetry is a property of the type rather than a gap in the implementation, and it is
unreachable for the ops this exists to map: the decoder's lost closures are CONSTANT functions
returning the same sentinel, so a mapper total on that one value is total everywhere.

## Source-breaking: `DagWire.encodeRecord` and `DagWire.contentFingerprint`

`DagWire.decodeRecord` has always taken the host codec's decode as a function; `encodeRecord`
hardwired the tier's own canonical op encoder, so the DAG wire had a host-owned path in one
direction and a tier-owned one in the other. Both now take the encode:

```fsharp
// before
DagWire.encodeRecord record
DagWire.contentFingerprint record

// after — `codec.EncodeOp`, or the canonical encoder directly
DagWire.encodeRecord CanonicalJson.encodeOp record
DagWire.contentFingerprint CanonicalJson.encodeOp record
```

**Pass `CanonicalJson.encodeOp` unless you know you want otherwise, and read this before you pass
`codec.EncodeOp` to `contentFingerprint`.** A record's fingerprint is a digest of its canonical wire
bytes, and a sink compares an arriving record against the fingerprints it has already STORED — so
changing which encoder mints them re-mints every fingerprint in a live store, and the first re-add
of an unchanged record after that reads as a content-address collision. Both sinks in this repo pass
the canonical encoder for exactly that reason. `OpJsonCodec.canonical`'s encode IS that function, so
a host using the reference codec has no choice to make.

## Verification

`Fuaran.UI.OpStream.Tests` → `Phase 1587 — TreeOp.mapMsg laws`: identity preservation over every
committed `ops/` fixture (with a second test asserting those fixtures COVER the `ops` family
`manifest.json` registers, so the claim is falsifiable), the typed and erased codecs agreeing on the
bytes, a refusal naming the op and the slot, the `Batch` index in the slot, and the go-red half —
a total mapper putting the host's own message on the tree.
