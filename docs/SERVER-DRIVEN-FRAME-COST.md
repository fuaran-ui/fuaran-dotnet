# What a server-driven frame costs

Phase 2526. A reading of the bytes and patches one write costs on the wire, taken before the lowering
changed, and the decision it supported. The figures are UTF-8 bytes of the SSE frame a session pushes
(`FrameWire.encodeSse`), through the real server renderer. They are deterministic, so the reading is a test:
`LoweringTests.fs`, "Phase 2526 - the frame-cost reading", prints every number below and asserts the shape of
each row. Run it with:

```powershell
dotnet run --project src/Fuaran.UI.ServerDriven.Tests -- --filter "Phase 2526 - the frame-cost reading"
```

## The tree

24 sites under one dashboard. A site is a card with a heading of its own, holding a `Heading` label, a bar
chart of 12 points and a static table of 50 rows by 4 columns. First paint is 478,894 bytes, and a frame with
no patches costs 62.

The phase asked for "the 24-site tree Phase 1762 uses". Phase 1762 has not shipped and holds no such tree. The
nearest thing, Phase 1761's cost reading, is a list of row-feed bindings with no tree around them, so the tree
above is written for this reading, on the same dashboard shape. It lives beside the reading.

## The floor

Every write below is one edit to site 7. The floor column is what the Phase 152 lowering shipped: one
`ReplaceFragment` per content op. It was read off the unmodified lowering first. Before the sites became cards
the same harness read 351, 22,186 and 20,893 bytes for label, style and row from the lowering itself, and the
floor the test now computes gives those same figures on that tree.

| Write | Ops | Patches | Frame bytes |
|---|---|---|---|
| Label: a leaf `Heading`'s text | `UpdateProp` | 1 `ReplaceFragment` | 351 |
| Style: the site card's text direction | `PatchStyle` | 1 `ReplaceFragment` | 22,277 |
| Row: one cell of the site's table | `EditNode`, `UpdateState` | 2 `ReplaceFragment` | 20,893 |
| Card heading: the site card's own heading | `UpdateProp` | 1 `ReplaceFragment` | 22,256 |

## Decision: go

`ReplaceFragment` is where the bytes go. It is every byte of every frame above except the 62 the envelope
costs, so the phase went ahead. The reading also says three things the phase did not assume.

**The floor is cheap on a leaf.** A label is 351 bytes, because the diff had already localised it. Granular
lowering takes that to 133, a real saving per write and a small one in the absolute.

**The row write paid for its re-render twice.** The diff spells a floor edit as `EditNode` then `UpdateState`,
even when no state moved. The two lowered to two `ReplaceFragment`s of one node, each about 10 KB, and the
second overwrote the first. That doubling was the largest avoidable cost in the reading.

**A write to a container costs its subtree.** The style and card-heading rows re-render the whole site. Neither
is a prop the body of the node shows, so neither is in the map below. See "What stays on the floor".

## After

| Write | Patches | Frame bytes | Before |
|---|---|---|---|
| Label | 1 `SetText` | 133 | 351 |
| Style | 1 `ReplaceFragment` | 22,277 | 22,277 |
| Row | 1 `ReplaceFragment` | 10,477 | 20,893 |
| Card heading | 1 `ReplaceFragment` | 22,256 | 22,256 |

The `DomPatch` vocabulary did not change. `SERVER_DRIVEN.md`, "What a content write lowers to", says what lowers
to what.

## The premise that did not hold

The phase took it that `SetText` and `SetAttr` worked and only lacked an emitter. They had never been emitted
for a node. A node's `data-fuaran-node-id` sits on its wrapper `<div>`, and the text and attributes a prop moves
sit on the body element inside it, so the shim's `e.textContent = …` on the wrapper would have erased the
heading and its tooltip hint, and a `SetAttr` would have landed on the wrapper. The shim now lands a
node-addressed granular patch on the wrapper's first element child. A form field is addressed by its own
marker and stays its own target.

That makes the body the only part of a node a granular patch can reach, which is what bounds the map.

## What stays on the floor

- A style edit. It moves the wrapper's class and any inline declarations, and it is the next candidate if style
  writes turn out to be common.
- A container's own heading (`Card`, `Disclosure`, `SummaryList`). It is a descendant of the body, not the body,
  and no patch can address it without a new address form, which is a vocabulary change. At 22 KB per write this
  is the largest cost the phase leaves in place.
- A row. The table is re-rendered once; there is no row-level patch.
- A bound text, a link's `Href`, and anything else whose value needs the host's sources or egress policy.

The falsifier for this decision is the table: a change that moved the floor's bytes out of `ReplaceFragment`, or
a tree whose writes are mostly leaf labels with large frames, would reopen it.
