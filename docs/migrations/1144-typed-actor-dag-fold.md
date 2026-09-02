# Migration — the typed-`Actor` DAG fold (0.48.0, Phase 1144)

**Old DAG content addresses do not carry forward.** That is the whole of it, and everything below is
either a consequence or a code edit. If you persist a DAG op-stream, read the *Persisted data*
section before upgrading.

Affected packages: `Fuaran.UI.OpStream.Dag.Abstractions`, `.Merge`, `.Sqlite`, `.InMemory`,
`.Inspect` (0.48.0), and the TypeScript `@fuaran-ui/ops` (0.20.0). The **linear** op-stream is
untouched: `OpRecord`, its hash chain, `chainFormatVersion` 2, and every non-DAG fixture family are
byte-identical to 0.47.0. The DAG is opt-in rung-4, so a consumer referencing none of the
`Fuaran.UI.OpStream.Dag.*` packages has nothing to do.

## What changed

`DagOpRecord` carried a bare `UserId: string`. It now carries the typed `Actor` the linear
`OpRecord` has carried since Phase 320 — `Human of id` | `Agent of model * version * id` — in the
same pinned `Actor.encode` bytes.

Because Phase 408 folded attribution **into** the DAG content address, typing it re-mints every
hash:

```text
pre-image, 0.47.0   …,"ts":1700000000,"userId":"u1","promptId":…,"result":…
pre-image, 0.48.0   …,"ts":1700000000,"actor":{"kind":"human","id":"u1"},"promptId":…,"result":…

wire record, 0.47.0  {"hash":…,"op":…,…,"tombstoned":false,"userId":"u1"}
wire record, 0.48.0  {"actor":{"kind":"human","id":"u1"},"hash":…,"op":…,…,"tombstoned":false}
```

The wire key moves to the **front** because top-level keys are Ordinal-sorted and `actor` sorts
before `hash`. The nested actor value is embedded verbatim in its own pinned member order (`kind`
first, then the case fields) — not re-sorted — exactly as the nested `op` is.

## Code changes

**Construction.** Wrap the id you were passing:

```fsharp
// 0.47.0
DagOpRecord.create streamId parents op promptId "alice" timestamp envelope
// 0.48.0
DagOpRecord.create streamId parents op promptId (Actor.Human "alice") timestamp envelope
// …or, if the id arrives as an untyped string from a host API:
DagOpRecord.create streamId parents op promptId (Actor.ofLegacyString userId) timestamp envelope
```

The same substitution applies to `DagOpRecord.createMerge`, `DagOpRecord.computeHash`,
`DagOpRecord.computeMergeHash`, and `GuestFork.genesis` / `GuestFork.step`. The parameter keeps its
position in every signature; only its type changes, so the compiler points at each site.

**Reads.** `record.UserId` becomes `record.Actor`. Where you genuinely want the bare attribution id —
a log line, a UI label — `Actor.id record.Actor` is the pre-1144 value exactly. Where you want the
Human/Agent distinction, it is now there to match on.

**Record literals.** `{ … ; UserId = "u1" ; … }` becomes `{ … ; Actor = Actor.Human "u1" ; … }`.
`DagGraphNode` in `Fuaran.UI.OpStream.Dag.Inspect` changes the same way.

**TypeScript.** `DagOpRecord.userId: string` becomes `actor: DagActor`, the exported
`{ kind: 'human'; id }` / `{ kind: 'agent'; model; version; id }` union. It is structurally identical
to `Actor` in `@fuaran-ui/op-stream` and assignable to and from it, so a host holding an op-stream
actor passes it straight in.

## Persisted data

**There is no in-place upgrade, and none is possible.** A record's hash IS its identity and its
parents' links; recomputing hashes under the new pre-image would rewrite every address in the store
and every parent reference to it, which is a new DAG rather than a migrated one.

What the read paths do, and deliberately:

- **`DagWire.decodeRecord` refuses a `userId` envelope by name**, naming this document. It does not
  lift it to `Human`. A lifted record would carry a stored `hash` that `DagOpRecord.recomputeHash`
  cannot reproduce, so the failure would surface later as an unexplained chain break instead of
  immediately as a clear refusal. `@fuaran-ui/ops` refuses identically.
- **`SqliteDagSink` still opens a pre-1144 database.** The `user_id` column is reused rather than
  renamed and now holds `Actor.encode` JSON; a pre-1144 row holds a bare id and reads back through
  `Actor.ofLegacyString` (the same fallback the linear `SqliteSink` has used since Phase 320).
  Opening is not validating: those records' content addresses are not valid under the new pre-image,
  and `DagVerify` will say so.

So the options for an existing store are the ordinary ones for a re-addressed content-addressed log:
keep it as a read-only archive under a 0.47.x reader; or replay its ops into a fresh 0.48.0 DAG,
which mints new addresses and is an intentional new history rather than a translation of the old one.

## Verifying the upgrade

The `wire-format-fixtures/dag/` corpus is the cross-host oracle and moved in the same change-set. A
host is conformant when it round-trips those four fixtures byte-identically — including the agent-actor
fixture, `dag-linear-step`, which is what pins the `agent` case's member order across hosts.
`DagVerify.chain` over a freshly written stream, and the hash-chain / tamper / checkpoint suites, are
the behavioural checks.
