# Fuaran.UI.Ops

Fuaran tree-op apply engine — the conversation-as-source-of-truth substrate. Implements §4g of the Fuaran design specification: a slim 10-op vocabulary the AI emits in lieu of full-tree regenerations, plus an `apply : TreeOp<'Msg> -> Node<'Msg> -> Result<Node<'Msg>, ApplyError>` engine that walks the tree by `NodeId` and returns either an updated tree or a §4d AI-recovery-shaped error payload.

## Surface

- `Fuaran.UI.Ops.Types` — `TreeOp<'Msg>` DU (`EditNode` / `UpdateProp` / `ReplaceBinding` / `UpdateStyle` / `UpdateState` / `InsertChild` / `RemoveNode` / `MoveNode` / `ReorderChildren` / `Batch`), `ApplyError`, `ApplyHint`, `ApplyErrorCode`.
- `Fuaran.UI.Ops.Apply` — `apply : TreeOp<'Msg> -> Node<'Msg> -> Result<Node<'Msg>, ApplyError>`. `Batch` is all-or-nothing — the first failing op aborts the batch and the tree is returned to its pre-batch state, with the error carrying the inner-op index.
- `Fuaran.UI.Ops.Introspect` — reflection-free shape helpers consumed by error rendering (kind name lookup, available-field enumeration per `NodeKind`, `nodes_with_<field>_field` tree search). Fable-compatible.
- `Fuaran.UI.Ops.ErrorRender` — turns an `(op, error)` pair into the §4d JSON shape (flat `code` / `message` / `hint.available_fields` / `hint.nodes_with_<field>_field` / `hint.suggestion`). Consumers (downstream AI consumers) parse this to drive AI retries.
- `Fuaran.UI.Ops.OpNotation` — a read-only, human-readable projection of ops and op batches. See below.

## Op-diff review notation

`OpNotation` renders `TreeOp` values as a terse, deterministic change-log — one line per op, `NodeId`-anchored, delta-only:

```
turn 12 · agent claude-fable-5 · 3 ops:
  revenue-kpi: Source → $query.netRevenue
  revenue-kpi: Label → "Net revenue"
  channel-grid: + child Metric "margin-kpi"
```

The full grammar is documented in the module's header doc-comment. Two properties are worth stating here:

- **It is output, not input.** There is no decoder, no round-trip obligation, and no wire-format change — the module exports `render` / `renderOps` / `renderTurn` / `renderTail` and nothing else. The canonical JSON stays the only interchange form; the notation is a review surface, so a notation change is a docs change rather than a conformance event.
- **It is delta-only.** An inserted or replacement subtree is *summarised* (kind, id, node count), never dumped. A reviewer wanting the contents reads the tree; this projection answers "what changed".

Intended consumers: inspector "ops" tabs alongside the tree / JSON views; live op-stream tail views over a sink; and PR review of committed op streams, where `renderTail` turns a diff of the stream into a diff of the changes. Erased values render as the canonical encoder's own sentinels (`<closure>`, `<opaque>`), so notation and JSON read with one vocabulary. Scalar payloads route through `Fuaran.Core.Canon`, so the projection is key-order independent and uses the pinned cross-host float layout.

## What's out of scope (v1)

- **Wire-format storage layer.** §4g's `Node<obj>` + `moduleMsgDecoder: JVal -> 'Msg` storage shape is a follow-up. v1 ships the typed in-memory apply (`TreeOp<'Msg>` over `Node<'Msg>`) the downstream AI consumer hooks into directly; JSONL persistence + decoder come with the per-turn op-log substrate.
- **Nested `UpdateProp` paths.** Top-level field paths (`"Label"`, `"Tone"`, `"Format"`, `"Rows"`, ...) ship working; nested paths like `"Columns.2.Label"` return a structured `PathNotSupportedYet` error with the supported field list, so an AI consumer gets a useful hint rather than a silent acceptance.
- **`UpdateProp` against `Action`-carrying or function-carrying fields.** `OnClick` / `OnRowClick` / `OnChange` / etc. are not editable via property paths; the AI re-emits the parent spec via `EditNode` to swap behaviour. Surfaced as `PathNotSupportedYet` with the supported field list.

Both gaps surface to the AI through the same §4d hint shape, so the closed loop can recover by picking a structural op instead.

Apache-2.0 licensed — see the repo [LICENSE](../../LICENSE).
