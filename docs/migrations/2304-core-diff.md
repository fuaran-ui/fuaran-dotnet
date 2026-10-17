# Migration — the tree diff is Fuaran.Core's `Diff` (Phase 2304)

`Fuaran.UI.OpStream.Replay` no longer carries a diff algorithm of its own. `TreeOpDiff.diff` lowers
`Fuaran.Core.Diff.toOpsContainedWith` over the tier's witnesses onto `TreeOp`, and `TreeDiff.diff` is
`Fuaran.Core.Diff.changes`. Two things move: the change **vocabulary** (a type change the compiler
points at) and the **scripts** the diff emits (a behaviour change no compiler sees). Rides the breaking
slot after 0.95.0.

## What changes

**`TreeDiff` takes Core's vocabulary.** `NodeChangeKind` and `NodeChange` are deleted;
`TreeDiff.Changes` is `Fuaran.Core.Diff.Change<NodeId> list` (`{ Id; Kind }`), in Core's order —
ascending id, then case order. Each old case and its new reading:

| Old `NodeChangeKind` | New `Diff.ChangeKind<NodeId>` |
|---|---|
| `Added(parent, position)` | `Added` — no payload; read the parent and position off the new tree |
| `Removed parent` | `Removed` — every id of a removed region, as before |
| `Moved(fromParent, fromPos, toParent, toPos)`, parent changed | `Moved(fromParent, toParent)` |
| `Moved(...)`, same parent, new position | **no entry on the child** — one `Reordered` on the PARENT |
| `KindChanged(fromKind, toKind)` | `KindChanged(fromKind, toKind)` — unchanged |
| `PropChanged(fromJson, toJson)` | `Changed` — encode the two shells yourself if you showed them |
| `TextChanged(fromText, toText)` | `Changed` — one entry; read the text off the two nodes |

Two roots with different ids (or a tree carrying an id twice) read as every old id `Removed` and every
new id `Added` — the reading of the `ReplaceRoot` the script is.

**`TreeOpDiff` emits Core's script.** A survivor that changed parent is a `MoveNode`, never a
`RemoveNode` + `InsertChild` that re-sends it; container content changes come first, then inserts and
moves, then removals, then reorders, then content changes of nodes that end as leaves. An added region
with no survivor below it is still ONE `InsertChild` of the whole subtree. A changed root id is
`[ ReplaceRoot after ]` (it was an `EditNode` at the old id, which did not round-trip). New:
`TreeOpDiff.tryDiff` answers Core's refusal under the tier's name (`DuplicateNodeId`), and
`TreeOpDiff.inverse` undoes an op — the structural five through `Ops.invert`.

## Code changes

```fsharp
// before
for c in diff.Changes do
    match c.Change with
    | NodeChangeKind.Moved(_, _, Some p, pos) -> showMove c.NodeId p pos
    | NodeChangeKind.TextChanged(a, b) -> showText c.NodeId a b
    | _ -> ()
// after (open Fuaran.Core)
for c in diff.Changes do
    match c.Kind with
    | Diff.ChangeKind.Moved(_, toParent) -> showMove c.Id toParent
    | Diff.ChangeKind.Reordered -> showReorder c.Id   // the parent
    | Diff.ChangeKind.Changed -> showChange c.Id
    | _ -> ()
```

A host lowering a WHOLE diff script to DOM patches lowers it from the pre-state:

```fsharp
// before
let patches = Lowering.lower render newTree (TreeOpDiff.diff oldTree newTree)
// after
let patches = Lowering.lowerScript render oldTree (TreeOpDiff.diff oldTree newTree)
```

`Lowering.lower` reads every op against the final tree, which is exact only when no later op
restructures what an earlier one addressed; Core's order (removals last) breaks that. The driver,
navigation and both Program drivers in this repository already call `lowerScript`. An undo witness
that applied an op and diffed back calls `TreeOpDiff.inverse op pre`.

## Verification

`Fuaran.UI.OpStream.Tests` (`TreeOpDiffEquivalenceTests`): every cross-host golden of
`diff/goldens.json` is emitted byte for byte; every script's structure is Core's op for op; no script
re-sends a survivor; the round-trip law over the corpus and 400 generated pairs. Measured with
`dotnet run -c Release -- core-diff` in `benchmarks/Fuaran.UI.Ops.Benchmarks`.

## Persisted data and rollback

Nothing persisted changes shape. Recorded ops still replay, recorded DAG merge nodes still verify (a
merge node's delta is replayed, not re-diffed). New merges mint deltas in the new shape, so their
hashes differ from what the previous diff would have minted for the same pair. Rollback is the
previous package version; a stream written since replays under it, because every op is a `TreeOp`
case it already applies.
