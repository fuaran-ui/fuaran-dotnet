# Fuaran error codes – cheat sheet

> Quick reference for AI authors and human developers. Every Fuaran error envelope carries a stable `Code` discriminator that's safe to pattern-match in your retry/recovery loop. Three families of codes; each is documented below with: **meaning**, **recovery strategy**, and a **canonical one-line example** of the situation that triggers it.
>
> **Wire shape**: every code is rendered as a flat-`"kind"`-tag JSON envelope per the Fuaran design specification §4d. The `Hint` block carries `available_fields` / `nodes_with_<field>_field` / `suggestion` to feed the retry loop.

---

## 1. `Fuaran.UI.Ops.ApplyErrorCode` – tree-op apply engine

Surfaced when `Fuaran.UI.Ops.Apply.apply : TreeOp<'Msg> -> Node<'Msg> -> Result<Node<'Msg>, ApplyError>` fails. 12 variants. Source: [`src/Fuaran.UI.Ops/Types.fs:103-140`](../src/Fuaran.UI.Ops/Types.fs).

| Code | Meaning | Recovery strategy | Example |
|---|---|---|---|
| **`NodeNotFound`** | The addressed `NodeId` is not in the tree. | Inspect the current tree via `fuaran.getNodeState` against a higher-level NodeId; correct the target. | `RemoveNode { id = NodeId "metric-xx" }` where no node has that id. |
| **`ParentNotFound`** | The addressed parent in `InsertChild` / `MoveNode` / `ReorderChildren` is not in the tree. | Walk the tree to find the intended parent; correct the `parent` id. | `InsertChild { parent = NodeId "panel-xx"; ... }` and the dashboard's children panel has a different id. |
| **`FieldNotFound`** | The `UpdateProp` path names a field that does not exist **at the failing segment** – a top-level field the spec record lacks, or a sub-field the addressed list element lacks (`Columns[0].Nope`). | Use `Hint.AvailableFields` (for a nested path it enumerates the sub-paths available at the failing segment); if `Hint.NodesWithField` is populated, pivot to one of those nodes; otherwise emit `EditNode` with a different `Kind`. | `UpdateProp { id = ...; path = "MaxValue"; ... }` against a `Display.Markdown` node; `UpdateProp { path = "Columns[0].Nope"; ... }` against a `DataGrid` (columns carry `Label` / `Format` / `Width`). |
| **`SlotNotFound`** | The `ReplaceBinding` slot path is not a Binding-typed slot on the target node. | Use `Hint.AvailableFields` to pick a real slot. | `ReplaceBinding { id = ...; slot = "Value"; ... }` against a `Layout.Stack` (Stack has no Binding slots). |
| **`KindMismatch`** | The op is structurally incompatible with the target – e.g. `MoveNode` would create a cycle; `ReplaceBinding`'s payload type doesn't match the slot's expected `'T`. | If cycle: pick a non-ancestor target. If type mismatch: re-emit with the correct `'T` shape. | `MoveNode` where source contains target. |
| **`ChildlessKind`** | The op targets a node whose `NodeKind` has no `Children` field (every Display / Input / Visualisation kind is childless). | Pick a Layout-category kind as the parent. | `InsertChild { parent = <Metric node id>; ... }`. |
| **`PositionOutOfRange`** | `position` is outside `0 ≤ position ≤ parent.Children.Length` (structural ops), **or** a nested `UpdateProp` path's list index is outside `0 ≤ i < list.Length`. | Clamp to the valid range the hint names (structural: `parent.Children.Length` appends; nested: the list's last valid index). | `InsertChild { position = 99; ... }` on a parent with 3 children; `UpdateProp { path = "Columns[5].Label"; ... }` on a grid with 2 columns. |
| **`OrderingMismatch`** | `newOrder` (in `ReorderChildren`) is not a permutation of the parent's current child ids – missing / extra / unknown ids. | Re-emit with exactly the parent's current child ids in the desired order; use `Hint` to see the expected set. | `ReorderChildren { newOrder = [a; b; c] }` when the parent has children `[a; b; c; d]`. |
| **`DuplicateNodeId`** | `InsertChild` would introduce a `NodeId` already present elsewhere in the tree. | Generate a fresh, unique `NodeId`. | Two simultaneous `InsertChild` ops both using `NodeId "new-metric"`. |
| **`PathInvalid`** | `path` (in `UpdateProp`) violates the path grammar (`WIRE_FORMAT.md` §3.4) – empty, empty segment, malformed `[i]` index (non-decimal, leading zero, missing `]`, the reserved `#` id-keyed form), or a list segment addressed without an index (`Columns.Label`). | Re-emit a grammatical path – dot-separated segments with 0-based `[i]` list indices (`Columns[0].Label`); `Hint.AvailableFields` enumerates the target kind's addressable paths when the node resolved. | `UpdateProp { path = "Columns[x].Label"; ... }`. |
| **`PathNotSupportedYet`** | The path is grammatical but the target kind/field has **no typed-traversal leg** in this engine version (nested addressing covers the `WIRE_FORMAT.md` §3.4 surface: `DataGrid.Columns[i]`, `Chart.YFields[i]`, `Tabs.TabHeaders[i]`, `Form.Fields[i]`; closure-bearing sub-fields are never addressable). | Use `Hint.AvailableFields` to see what *is* supported for the target kind; emit a structural op (`EditNode` / `ReplaceBinding`) instead. | `UpdateProp { path = "Columns[0].Kind"; ... }` – a column's cell kind is closure-bearing; swap the node via `EditNode`. |
| **`BatchAborted(innerIndex)`** | An inner op of a `Batch` failed at the given 0-based index; the batch was rolled back. | Inspect the inner op at `innerIndex`; fix or remove it; resubmit the batch. | Batch with 5 ops; op at index 2 fails. |

### `Fuaran.UI.Ops.ApplyHint` – what the `Hint` block carries

| Field | When populated | What it carries |
|---|---|---|
| `NodeKind: string option` | When the engine resolved the target node. | `"Metric"`, `"DataGrid"`, `"Dashboard"`, etc. |
| `AvailableFields: string list` | `FieldNotFound` / `SlotNotFound` / `PathNotSupportedYet`, and the nested-path failures (`PathInvalid` / `PositionOutOfRange`) when the target node resolved. | The supported field / slot names on the target kind – for a nested-path failure, the sub-paths / patterns available at the failing segment (e.g. `Label`, `Format`, `Width` at `Columns[0].…`). |
| `NodesWithField: (string * NodeId list) option` | `FieldNotFound` when the field exists on *some* other node. | `("MaxValue", [NodeId "metric-revenue"; NodeId "metric-margin"])` – pivot to one of these. |
| `Suggestion: string option` | When the engine can name a likely fix. | Free-text – e.g. `"Use 'Tone' instead of 'Status' for callout styling."` |

---

## 2. `Fuaran.UI.AiTools.IntrospectErrorCode` – runtime-introspection tools

Surfaced by `fuaran.getNodeState` / `getBindingValue` / `getRenderedDom` / `getRuntimeErrors` when a tool call can't proceed. 4 variants. Source: [`src/Fuaran.UI.AiTools/Types.fs:265-291`](../src/Fuaran.UI.AiTools/Types.fs).

| Code | Meaning | Recovery strategy | Example |
|---|---|---|---|
| **`NodeNotFound`** | The addressed `NodeId` is not present anywhere in the tree. | Inspect a parent / sibling via a higher-level `getNodeState` call; correct the id. | `fuaran.getNodeState { id = "metric-revenue-typo" }` after a recent rename. |
| **`SlotNotFound`** | The addressed `slot` (in `getBindingValue`) is not a Binding-typed slot on the node's kind. | Use `Hint.AvailableFields` for the supported slot names on that kind. | `fuaran.getBindingValue { id = "stack-1"; slot = "Value" }` – Stack has no binding slots. |
| **`UnknownIncludeKey`** | The `include` filter contained a key the engine doesn't know. v1 cannot trigger this (the F# enum constrains it). Reserved for the wire-form ingest path. | Use one of the five supported keys: `Props`, `Bindings`, `CurrentState`, `StateDetail`, `Geometry`. | `fuaran.getNodeState { include = ["StyleOverrides"] }` – not a valid `IncludeKey`. |
| **`ProbeUnwired`** | The renderer-state probe (current-state / geometry) is not wired – the host hasn't supplied an `IGeometryProbe` / `IRuntimeErrorSink` / `ICurrentStateProbe`. Returned only for fields explicitly asked for via `IncludeKey`. | If the host doesn't wire the probe, omit that `IncludeKey` from your call; alternatively, escalate to the orchestrator to request the host wire it. | `fuaran.getNodeState { include = ["Geometry"] }` in a host that hasn't bound `IGeometryProbe`. |

---

## 3. `Fuaran.UI.AiTools.BindingErrorCode` – binding-resolution failures

Surfaced inside `getNodeState`'s `Bindings` map and `getBindingValue`'s `ResolvedBindingResult.Failed`. 4 variants. Source: [`src/Fuaran.UI.AiTools/Types.fs:116-130`](../src/Fuaran.UI.AiTools/Types.fs).

| Code | Meaning | Recovery strategy | Example |
|---|---|---|---|
| **`SourceUnregistered`** | The `Query` / `Filter` / `Selection` / `State` data source was not registered at all – the host hasn't provided a value for this binding's source. | Either wait for the host to register the source, or pivot the tree to use a static value (`Binding.Static`). | `Binding.Query "revenue"` and `BindingSources.QueryResults` has no `"revenue"` entry. |
| **`NotResolvedYet`** | The data source is registered but the value has not arrived yet (pending Query / async fetch). | Re-call `fuaran.getBindingValue` after a delay; the orchestrator's normal polling cadence handles this. | `Binding.Query "revenue"` registered with pending result. |
| **`AccessorThrew`** | The Binding's typed accessor closure threw on the registered value (expected `'a`, got something else). | Check the source's actual shape via the host's schema; correct the accessor. | `Binding.Query("revenue", fun r -> r.Total)` where `r` has no `Total` field. |
| **`TypeMismatch`** | The `Static` / `State` default did not unbox to the expected `'T` at the renderer boundary. | The expected `'T` is per-slot per-kind; check the type contract and emit a `ReplaceBinding` with the correct `'T`. | `Binding.Static (42 : int)` on a slot expecting `float`. |

### `Fuaran.UI.AiTools.BindingResolutionHint`

| Field | When populated | What it carries |
|---|---|---|
| `NodeKind: string option` | When the addressed node is reachable. | `"Metric"`, `"DataGrid"`, etc. |
| `Suggestion: string option` | When the engine can name a fix. | `"Wait for query to complete"`, `"Check the filter store has a value for this key"`, etc. |
| `AvailableAlternatives: string list` | Other binding slots on the same node, or static-fallback shapes the spec record allows. | `["Value (Static)"; "Goal (Static)"]` for a Metric whose Query slot failed. |

---

## 4. `Fuaran.UI.Validator` `FUARAN###` codes – the reserved pack/host band

The build-time validator's findings carry `FUARAN###` codes (e.g. `FUARAN050` `ScalarRangeCheck`, `FUARAN069` inert-control, `FUARAN073` fire-and-forget-call, `FUARAN084` `WireSurvivabilityCheck` – a `Binding.Computed` host-only escape, advisory when hand-authored and Error in an orchestrated / AI-emitted run (`--orchestrated`); see `WIRE_FORMAT.md` §5.1). The spec's own rules occupy the **`FUARAN0xx`** band; each rule's meaning + severity is declared where the rule ships (`STABILITY.md` and the [`VOCABULARY.md`](VOCABULARY.md) charter), and the cross-implementation surface is enumerated in the TypeScript tier's validator (`@fuaran-ui/validator`).

**Suppressing a validator finding.** Source that deliberately holds a rejected shape — canonically a negative test asserting the runtime reports the defect — opts out with a comment pragma: `// fuaran-validator: disable FUARAN047, FUARAN048 — reason` (file-scoped) or `// fuaran-validator: disable-next-line FUARAN044` (the following line only). Suppressed findings are counted in the run summary rather than hidden, and only the reporting layer is filtered — every check still runs. This is a **host-side** mechanism on the .NET validator, not part of the cross-implementation spec surface. See the [validator README](../src/Fuaran.UI.Validator/README.md#suppressing-a-finding).

**Reserved band – `FUARAN2xx` = host/pack-assigned.** Codes in the `FUARAN200`–`FUARAN299` band are **reserved for rules a host or a rule-pack contributes**, layered atop the spec's own families. The spec will never mint a `FUARAN2xx` code, so a pack can assign in this band without colliding with a future spec rule. This is the concrete, per-domain expression of `Fuaran.Core.Validator`'s pack-provenance convention (a pack rule's family id is `pack + "/" + ruleId`, so the contributing pack is recoverable from any finding). A host that surfaces both spec and pack findings can therefore partition them by band (`0xx` = spec, `2xx` = pack/host) and attribute each pack finding by its `/`-delimited family id – pack-layering stays legible and certifiable through the public validator framework without the framework shipping any pack content.

---

## Reading an error envelope (canonical shape)

Every Fuaran error renders to the same flat-`"kind"`-tag JSON envelope:

```json
{
  "op": {
    "type": "UpdateProp",
    "id": "metric-revenue",
    "path": "MaxValue"
  },
  "error": {
    "code": "FieldNotFound",
    "message": "Field 'MaxValue' does not exist on Metric",
    "hint": {
      "node_kind": "Metric",
      "available_fields": ["Value", "Goal", "Tone", "Format", "Tooltip"],
      "nodes_with_field": {
        "field": "MaxValue",
        "node_ids": ["progress-onboarding", "progress-billing"]
      },
      "suggestion": "Pivot to one of the Progress nodes via UpdateProp on its 'MaxValue' field, or use UpdateProp on Metric's 'Goal' field for the equivalent semantic."
    }
  }
}
```

The Code identifies the failure class; the `Hint` block carries enumerated recovery options. AI authors should pattern-match `error.code` (stable across releases) for the retry strategy and consult `error.hint.suggestion` for the prose hint.

---

## Decision tree – "what should I do with this error?"

```
error.code == "NodeNotFound"
  → call fuaran.getNodeState on a parent to inspect the current tree;
    correct the id.

error.code == "FieldNotFound" / "SlotNotFound"
  → inspect error.hint.available_fields;
    if error.hint.nodes_with_field is populated, pivot to one of those nodes;
    otherwise emit EditNode or ReplaceBinding with a different shape.

error.code == "PathInvalid"
  → the path violates the grammar (WIRE_FORMAT.md §3.4); re-emit with dot
    segments + 0-based [i] list indices, e.g. Columns[0].Label.

error.code == "PathNotSupportedYet"
  → the target kind/field has no typed-traversal leg (nested addressing covers
    Columns[i] / YFields[i] / TabHeaders[i] / Fields[i]); emit a structural op
    (EditNode / InsertChild / ReplaceBinding) instead.

error.code == "BatchAborted"
  → inspect the inner op at error.hint.inner_index (or the message body);
    fix or remove that one op; resubmit the batch.

error.code == "KindMismatch"
  → if it's a cycle (MoveNode): pick a non-ancestor target.
    if it's a type mismatch (ReplaceBinding): re-emit with the correct 'T.

error.code == "SourceUnregistered" (binding)
  → either wait for the host to register, or emit ReplaceBinding to use
    Binding.Static.

error.code == "NotResolvedYet" (binding)
  → re-poll after a delay; the orchestrator's normal cadence handles this.

error.code == "ProbeUnwired"
  → omit that IncludeKey, or escalate to the orchestrator.
```

---

## See also

- [`AI_AUTHORING_GUIDE.md`](AI_AUTHORING_GUIDE.md) – the comprehensive AI-author orientation, with worked examples of the closed-loop recovery shape.
- [`TECHNICAL_GUIDE.md`](TECHNICAL_GUIDE.md) – the human-author reference.
- The Fuaran design specification §4d – canonical error format specification.
- Source: [`src/Fuaran.UI.Ops/Types.fs`](../src/Fuaran.UI.Ops/Types.fs), [`src/Fuaran.UI.AiTools/Types.fs`](../src/Fuaran.UI.AiTools/Types.fs).
