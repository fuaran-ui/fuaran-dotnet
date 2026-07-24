# Fuaran.UI.Ops

Fuaran tree-op apply engine — the conversation-as-source-of-truth substrate. Implements §4g of the Fuaran design specification: a slim 10-op vocabulary the AI emits in lieu of full-tree regenerations, plus an `apply : TreeOp<'Msg> -> Node<'Msg> -> Result<Node<'Msg>, ApplyError>` engine that walks the tree by `NodeId` and returns either an updated tree or a §4d AI-recovery-shaped error payload.

## Surface

- `Fuaran.UI.Ops.Types` — `TreeOp<'Msg>` DU (`EditNode` / `UpdateProp` / `ReplaceBinding` / `UpdateStyle` / `UpdateState` / `InsertChild` / `RemoveNode` / `MoveNode` / `ReorderChildren` / `Batch`), `ApplyError`, `ApplyHint`, `ApplyErrorCode`.
- `Fuaran.UI.Ops.Apply` — `apply : TreeOp<'Msg> -> Node<'Msg> -> Result<Node<'Msg>, ApplyError>`. `Batch` is all-or-nothing — the first failing op aborts the batch and the tree is returned to its pre-batch state, with the error carrying the inner-op index.
- `Fuaran.UI.Ops.Introspect` — reflection-free shape helpers consumed by error rendering (kind name lookup, available-field enumeration per `NodeKind`, `nodes_with_<field>_field` tree search). Fable-compatible.
- `Fuaran.UI.Ops.ErrorRender` — turns an `(op, error)` pair into the §4d JSON shape (flat `code` / `message` / `hint.available_fields` / `hint.nodes_with_<field>_field` / `hint.suggestion`). Consumers (downstream AI consumers) parse this to drive AI retries.

## What's out of scope (v1)

- **Wire-format storage layer.** §4g's `Node<obj>` + `moduleMsgDecoder: JVal -> 'Msg` storage shape is a follow-up. v1 ships the typed in-memory apply (`TreeOp<'Msg>` over `Node<'Msg>`) the downstream AI consumer hooks into directly; JSONL persistence + decoder come with the per-turn op-log substrate.
- **Nested `UpdateProp` paths.** Top-level field paths (`"Label"`, `"Tone"`, `"Format"`, `"Rows"`, ...) ship working; nested paths like `"Columns.2.Label"` return a structured `PathNotSupportedYet` error with the supported field list, so an AI consumer gets a useful hint rather than a silent acceptance.
- **`UpdateProp` against `Action`-carrying or function-carrying fields.** `OnClick` / `OnRowClick` / `OnChange` / etc. are not editable via property paths; the AI re-emits the parent spec via `EditNode` to swap behaviour. Surfaced as `PathNotSupportedYet` with the supported field list.

Both gaps surface to the AI through the same §4d hint shape, so the closed loop can recover by picking a structural op instead.

Apache-2.0 licensed — see the repo [LICENSE](../../LICENSE).
