# Where a server-driven state write spends its time

Phase 1762. A measurement taken before building anything, and the decision it supports.

**Decision: no-go.** The `Driver` step is not restricted to the binding graph's dirty set. Resolution is
not the stage where the time goes. The restriction has no sound footing in the hand-authored step. On the
write a live board takes most, the restriction would also skip nothing. The reasons are below, with the
evidence that would have to change before the question is worth reopening.

## What was measured

A generated live operations board: **24 sites over one shared orders table** of 1,000 or 10,000 rows
(`id`, `site`, `value`). Each site shows two things:

- its **target**, a one-key read of `target-<site>`;
- its **league table**, the top ten of its own orders by value, which is a live `Transform`
  (`Filter site = i`, `Sort value desc`, `Limit 10`) over `$state.orders`.

Two writes are measured:

| write | what it is | binding cone on the board |
|---|---|---|
| one key (`target-03`) | one site's target changes | site 3's metric alone: 0 of 24 league tables, 1 card |
| one order (site 05) | site 5's best order is raised | **every** league table: 24 of 24, 24 cards |

The step is `Driver.applyResolvedActions`, the hand-authored loop. Its view is the shape that loop needs
to show a change: the view **bakes** the values it resolved into the tree, because the step's diff is
binding-blind (`SERVER_DRIVEN.md`, "Why re-resolve into the tree"). The stages are timed separately:

| stage | what is timed |
|---|---|
| resolve (full) | the 24 league tables through the renderer's own resolver (`BindingResolver.resolve`), no store |
| resolve (session store) | the same through a session's `LiveTransformStore` (identity `id`), primed over the board before the write |
| view | the board's structure around values already resolved |
| diff | `TreeOpDiff.diff` over the old and new trees |
| lower | `Lowering.lowerScript` over the diff's ops, rendering fragments with the server renderer |
| step | the whole `Driver` step, in which the view resolves without a store |

The remaining columns are the **counterfactual restricted step**. They are measured, not built. The step
resolves only the league tables in the write's cone (`BindingGraph.dirty` over the board with its
bindings left in the tree) and diffs only the site cards holding a cone reader. It is checked against the
full path: "identical" means that lowering the restricted ops gives the same patches as lowering the full
diff.

To reproduce:

```powershell
$env:FUARAN_STEP_COST = "1"
dotnet run --project src/Fuaran.UI.ServerDriven.Tests -c Release -- --filter "Phase 1762" --sequenced
```

The harness is `src/Fuaran.UI.ServerDriven.Tests/RestrictedStepTests.fs`. Each figure is the median of 51
runs after 25 warm-up runs, in milliseconds. The data is deterministic.

## The breakdown

Two runs on one 8-core development machine, 2026-11-19, Release build. Other builds were running on the
machine during both runs, so read the **ratios**, not the absolute figures. The second run is about 1.5×
the first throughout, and the order of the stages is the same in both.

Run 1:

| rows | write | resolve (full) | resolve (session store) | view | diff | lower | step | ops / patches | tables consulted / skipped | cards diffed | resolve (cone) | diff (cone) | graph + cone | cone patches identical |
|---:|---|---:|---:|---:|---:|---:|---:|---|---|---:|---:|---:|---:|---|
| 1000 | one key (target-03) | 1.540 | 0.409 | 0.250 | 5.509 | 0.073 | 7.561 | 1 / 1 | 0 / 24 | 1 | 0.015 | 0.197 | 1.289 | true |
| 1000 | one order (site 05) | 0.726 | 10.140 | 0.143 | 2.130 | 0.144 | 4.269 | 2 / 1 | 24 / 0 | 24 | 0.655 | 1.692 | 0.677 | true |
| 10000 | one key (target-03) | 2.042 | 0.382 | 0.096 | 2.610 | 0.048 | 4.083 | 1 / 1 | 0 / 24 | 1 | 0.008 | 0.139 | 0.412 | true |
| 10000 | one order (site 05) | 2.386 | 65.321 | 0.137 | 2.881 | 0.172 | 5.819 | 2 / 1 | 24 / 0 | 24 | 2.731 | 2.121 | 0.511 | true |

Run 2:

| rows | write | resolve (full) | resolve (session store) | view | diff | lower | step | ops / patches | tables consulted / skipped | cards diffed | resolve (cone) | diff (cone) | graph + cone | cone patches identical |
|---:|---|---:|---:|---:|---:|---:|---:|---|---|---:|---:|---:|---:|---|
| 1000 | one key (target-03) | 2.063 | 0.444 | 0.381 | 9.187 | 0.096 | 10.648 | 1 / 1 | 0 / 24 | 1 | 0.014 | 0.169 | 1.611 | true |
| 1000 | one order (site 05) | 1.146 | 16.198 | 0.158 | 4.053 | 0.229 | 5.414 | 2 / 1 | 24 / 0 | 24 | 0.898 | 2.494 | 0.957 | true |
| 10000 | one key (target-03) | 3.220 | 0.415 | 0.099 | 3.287 | 0.092 | 9.933 | 1 / 1 | 0 / 24 | 1 | 0.028 | 0.262 | 0.967 | true |
| 10000 | one order (site 05) | 2.976 | 82.025 | 0.137 | 3.582 | 0.254 | 10.793 | 2 / 1 | 24 / 0 | 24 | 3.681 | 3.620 | 1.133 | true |

The first row of each run includes some first-measurement noise. Its diff is the same tree as the third
row's, and it reads higher in both runs.

## What the breakdown says

1. **The diff is the largest stage, and resolution comes second.** A step costs about 4 to 11 ms. The diff
   takes about half of that, resolving the 24 league tables takes about a third, and view and lowering
   each take under 0.4 ms. The diff costs the same whether one node changed or several. It encodes every
   node's canonical shell on every step, so its cost follows the size of the tree, not the size of the
   change. The row count barely moves it, because the baked tree holds ten rows per site whatever the
   table holds.
2. **Resolution grows with the table, but slowly.** Evaluating 24 pipelines over 10,000 rows costs
   2 to 3 ms, against 0.7 to 2 ms at 1,000 rows.
3. **The session store makes the board's main write 15 to 27 times more expensive.** A write that hands the
   store a new table, which is how this host writes, makes **every site diff the whole table** against the
   one it holds (`Delta.diff`, once per site). On 24 sites over 10,000 rows that costs 65 to 82 ms, against
   2.4 to 3 ms for full evaluation. On the one-key write, where the table object is unchanged, the store is
   the cheap path (0.4 ms, every site reused). An edit recorded with the store
   (`LiveTransform.applyEdit`, Phase 1891) derives its delta from the op and does not diff. That path was
   not measured here.
4. **The cone helps only the one-key write.** For `target-03` the restricted step resolves no league table
   and diffs one card: about 0.2 ms of work against 4 to 11 ms. For a write to the shared table, the cone
   is the whole board, so the restricted step does all of the work. The graph is keyed by channel, not by
   row. A write to `$state.orders` is a write to everything that reads the table, even when only site 5's
   top ten moves. Building the graph and taking the cone costs 0.4 to 1.6 ms per write in addition, unless
   the graph is cached across steps whose structure did not move.
5. **Where the restriction applies, it was identical.** On both writes, at both scales, the restricted ops
   lowered to the same patches as the full diff.

## Why no-go

- **Resolution is not where the time goes.** It is about a third of the step. The phase set its own test:
  if resolution is not where the time goes, the restriction is the wrong work.
- **The cone has no sound footing in the hand-authored step.** The `Driver` resolves nothing on its own. A
  binding is resolved only when the lowering renders a node the diff changed. So "clean sites skip their
  store consult" is already true of this step: a league table whose node did not change is never
  rendered. The test *a Driver step renders, and consults the store for, only the nodes its diff changed*
  pins this. Restricting the diff would need the cone to describe what the view changed, and it cannot do
  so for either shape the view can take:
  - a view that bakes its values (the shape the step needs in order to patch anything) leaves no state
    binding in the tree, so **the cone of every write over it is empty**, and a step restricted by it
    would skip the very site that changed (*a baked board carries no binding the cone can name*);
  - a view that leaves its bindings in the tree emits **no op at all** for a write that lands only in the
    host's sources, because the diff is binding-blind, so there is nothing to restrict (*a bound write the
    view does not reflect produces no op*).

  The view is an arbitrary function of the model, and the step cannot tell, short of running the view and
  diffing, whether the view branched on a write. A restriction that decides this from the bindings would
  be unsound. The phase allowed no unsound restriction ("shipped only if identical").
- **The saving is on the wrong write.** A live board takes mostly data writes, and on this board a data
  write's cone is every site. The one write the cone shortens, a single key read by one site, is the
  write that was already cheap to patch.

## What would change the answer

The restriction becomes the right work where all three of the following hold. None of them holds for the
`Driver` step.

- **The driver owns resolution**, as the bounded driver does. There the tree is fixed and bound, every
  step re-resolves it against the state store, and the update *is* the state write. In that shape
  "resolve" is the whole board on every step, and the cone of a written key is exact. That is where a
  cone-restricted re-resolve and diff can be sound. Its cost should be measured on that driver before
  anything is built.
- **The cone is finer than a channel.** A write to a shared table dirties every reader of it. Only a
  row-aware cone could restrict the main write. The columnar substrate's incremental seam already knows
  which rows a delta touches. The binding graph does not.
- **The diff stops paying for unchanged subtrees.** The diff is the largest stage and is proportional to
  the tree. A short-circuit for subtrees that are the same object on both sides is sound for any view,
  baked or bound, with no cone. It needs no assumption about what the view read, and a host that reuses
  unchanged site subtrees would gain the restricted diff for free.

Separately, the store's per-site re-diff of a shared source (finding 3) is a cost the store imposes on any
board whose sites share a table.

## The facts the decision stands on

`RestrictedStepTests.fs` pins them on every run, so the decision goes red if the step changes under it:

| test | what it pins |
|---|---|
| a one-key write's cone is the one reader of that key, and nothing else | `target-03` dirties `site-03-target` alone, with an exact verdict |
| a write to the shared table puts every league table in the cone | `$state.orders` dirties all 24 league tables |
| a baked board carries no binding the cone can name | the cone over the tree the `Driver` holds is empty for both writes |
| a Driver step renders, and consults the store for, only the nodes its diff changed | one rendered node, one patch, zero store consults on the one-key write |
| a bound write the view does not reflect produces no op | no patch, no render, no store consult when the write reaches only the host's sources |
