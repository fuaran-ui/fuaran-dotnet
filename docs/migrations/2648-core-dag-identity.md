# Migration — the UI op-stream DAG is Core's DAG (0.104.0, Phases 2645 and 2648)

**Every stored DAG address moves, once.** A DAG node is a `Fuaran.Core.DagNode` whose op is the linear chain's
`StreamEntry`, addressed by Core's `Dag.nodeId` (`CRYPTO.md`, `WIRE_FORMAT.md` §5.3.3). A merge node is an ordinary
node whose op is its reconciliation script. No pre-cut address recomputes, and nothing dual-reads: `SqliteDagSink`
refuses a database holding the pre-cut `dag_op_record` table, and `DagWire.decodeRecord` refuses a record carrying
`outcomeHash` or `tombstoned`, both by name. The linear op-stream and its packages are byte-unchanged.

Affected packages: `Fuaran.UI.OpStream.Dag.Abstractions`, `.Dag.InMemory`, `.Dag.Sqlite`, `.Dag.Merge`,
`.Dag.Inspect`. The full surface list is `STABILITY.md` 0.104.0, "Phase 2645".

## What changes, by file a consumer touches

| Where the consumer used | 0.103.x | 0.104.0 |
|---|---|---|
| record construction (`DagOpRecord.fs`) | `{ StreamId = …; Hash = …; … }`, `computeHash`, `createMerge` | `DagOpRecord.create` / `ofEntry`; the node is `r.Node`, and `r.Hash`, `r.Parents`, `r.Op`, … are read members |
| merge outcome (`DagOpRecord.fs`) | `OutcomeHash`, `computeMergeHash` | none: Core derives a merge's state by folding the head's closure |
| pruning (`IDagOpStreamSink.fs`) | `Tombstoned` on the record, `Lca` | `Tombstone` moves the node to `Skeleton` (address and parents); `Fuaran.Core.Dag.mergeBase` |
| topology, retention, replay | `DagTopology`, `DagRetention.liveSet` / `prunable`, `DagReplay`, `DagCheckpoint` | Core's `Dag.ancestorsOf` / `mergeBase` / `prunable`; `DagFold` (replay, checkpoints, compaction) |
| merging (`DagMerge.fs`) | `TreeMerge`, `mergeLookups`, `mergeGated*`, `recordAuthor`, `MergeConflict`, `DagPrimacy`, `ReassertPin` | `DagMerge.mergeDag` over a `MergeContext`; refusals are `ReconcileFault`; precedence is `MergeContext.Precedence` |
| results | `NeedsManualMerge`, `Conflicted`, `NeedsThreeWayMerge` | `Refused of ReconcileFault`, `MintRefused` |
| inspect (`DagAudition`, `DagCoordinateDiff`, `DagGraphModel`, `DagOverlay`) | lookups, `OutcomeHash` | a Core DAG; the skeleton is drawn as `Tombstoned`; `Actor` / `Timestamp` are options |
| SQLite schema | `dag_op_record`, `dag_head` | `dag_schema` (version 1), `dag_node`, `dag_skeleton`, `dag_head` |

```fsharp
// 0.103.x
let r = { StreamId = s; Hash = DagOpRecord.computeHash ps op ts actor None ok; Parents = ps; Op = op; … }
match DagMerge.merge (fun _ -> MergeAuthor.Primary) sink s initial a b now |> Async.RunSynchronously with
| MergeResult.Conflicted conflicts -> …
// 0.104.0
let r = DagOpRecord.create s ps op None actor ts ok
match DagMerge.merge sink s initial a b now |> Async.RunSynchronously with
| MergeResult.Refused fault -> …   // ReconcileFault: Refused pairs, OrderSensitive, IntroducedDefects, …
```

## Re-minting a store (once, before the first open under 0.104.0)

`tools/dag-remint` re-addresses a pre-cut store. It shipped in the source tree at `v0.104.0` and is **not in any later
tree** (Phase 2697 removed it): check out the `v0.104.0` tag, run it there, and then move to a later release. A store
not re-minted before that move cannot be re-addressed by this repository again.

```powershell
# from a checkout of tag v0.104.0
dotnet run --project tools/dag-remint -- sqlite app.db --initial initial.json --receipt remint-receipt.json
dotnet run --project tools/dag-remint -- wire records.jsonl records-0.104.jsonl --initial initial.json
```

- **What it does.** Every live record's stored address is first recomputed under the pre-cut rule, and a store
  where one does not recompute is refused. Each record is then re-addressed under `Dag.nodeId` with its envelope
  carried byte for byte. A merge is re-run through `DagMerge.mergeDag` over its re-minted parents and must reach the
  tree it committed to. Tombstoned records go to the skeleton with their old address. The SQLite form writes
  `<db>.remint`, reads it back verified, and then replaces the database, keeping the original as `<db>.pre-2645`.
- **`--initial`** is the tree the stream's history starts from (canonical node JSON). It is needed only when the
  store holds a merge. **`--precedence a|b`** re-asserts one side when an original merge resolved by precedence.
- **Idempotent.** A store already at the cut's schema is verified, reported `"alreadyCurrent":true`, and left
  untouched. Exit 0 means re-minted or current; 1 means refused, with the record named and nothing written.
- **Refusals:** a pre-cut address that does not recompute, a parent the store does not hold, a merge with no
  `--initial`, a merge the policy refuses, a merge whose re-run reaches another tree, a mixed-generation set.
- **A host with its own `'Msg`** calls `SqliteRemint.remint` / `WireRemint.remint` with its own `IOpJsonCodec`. The
  CLI decodes ops through the tier's canonical codec. Because every address is recomputed first, an op that codec
  does not round-trip is refused rather than re-addressed.
- **The receipt** gives each stream's old and new head and every old and new address. It is a recomputation, not
  a signature (`CRYPTO.md`).

## Rollback

Restore `<db>.pre-2645` over the database, or keep the pre-cut wire file, and pin the DAG packages back to
0.103.x. Nothing else moved: the linear stores and their checkpoints are not touched by either phase.
