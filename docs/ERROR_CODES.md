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

**Two families share this band, and only one of them is the build-time walker.** The paragraph
above describes the walker, which reads F# *source* — but the larger family by far is the
**pre-emit validator**, which reads a *tree* just before it goes on the wire and raises the whole
`FUARAN047`–`FUARAN163` range. A code you have in hand belongs to whichever family reported it, and
the two are enumerated in different places: the pre-emit family's codes, severities and message
shapes are published as data in the conformance corpus's `validator/defect-vocabulary.json`
(generated from the reference host, never hand-maintained) and as the table in
[§5](#5-pre-emit-validator-codes--the-full-table) below, generated from the same source; each host
declares which of them it implements in its own `validator-coverage.json`. The walker's family is
enumerated by hand in the table below; a test in the validator's own suite fails when the walker's
sources emit a code that has no compiled snippet making it fire.

**The build-time walker's codes (Phase 2053).** Every code here fires on source that compiles
against the current types. The column on the right says whether the tree-time validator covers
the same defect; where it does, the build-time rule has been retired in its favour.

| Code | Severity | Fires on | Tree-time coverage |
|---|---|---|---|
| `FUARAN001` | Error | Two calls in one tree with the same NodeId literal. A tree is the subtree under an outermost constructor that takes a child node (`Fuaran.box`, `stack`, `card`, `dashboard`, …) | `FUARAN-DUP-ID`, one emitted tree at a time; the build-time rule stays because it sees every tree a source can build |
| `FUARAN002` | Warning | The same NodeId literal in two different trees | None: the tree-time validator sees one tree |
| `FUARAN010` | Error | `binding.query "name"` naming a query the manifest does not declare | None |
| `FUARAN020` | Error | `Action.dispatch` of a Msg case the manifest does not declare | None |
| `FUARAN030` | Warning | A grid whose source query has no `queryRowTypes` entry | None |
| `FUARAN031` | Error | A grid whose `toRow` parameter is annotated with a type other than the manifest's row type | None |
| `FUARAN042` | Error | `binding.local` with `format = None` | None |
| `FUARAN043` | Warning | `binding.local` flushing `OnCommitAction` in a project with no `Action.CommitLocal` | None |
| `FUARAN044` | Error | `binding.local` outside a Text / Number / RangedNumber field (case or smart constructor) | None |
| `FUARAN045` | Warning | A segmented choice with more than 7 static options | None |
| `FUARAN046` | Warning | `Fuaran.gridLayoutTemplated` with a `repeat(N, 1fr)` template | None (`FUARAN144` is a different defect: a malformed track list) |
| `FUARAN050` | Warning | A static progress `Fraction` outside `[0, 1]` | None |
| `FUARAN051` | Warning | A static ranged-number value outside its `min` / `max` | None |
| `FUARAN053` | Warning | A Custom node declaring interior ids with no matching `RegisterCustomRenderer` | None |
| `FUARAN054` | Warning | The project's Custom-node ratio above `customNodeRatio` | None |
| `FUARAN055` | Warning | A Custom node with no `contentHash` | None |
| `FUARAN056` | Error | Two fragment declarations with one name | None |
| `FUARAN057` | Error | A fragment reference no declaration names | None (the renderer substitutes a placeholder) |
| `FUARAN058` | Error | A fragment reference cycle | None (the renderer's cycle guard substitutes a placeholder) |
| `FUARAN059` | Error | A `Repeat` hole with an unbounded count space | None |
| `FUARAN060` | Warning | `Node.withExtraAttribute` with a key outside `data-*` / `aria-*` | None (the renderer drops the entry) |
| `FUARAN061` | Error | A blank ISO-4217 currency code | None |
| `FUARAN062` | Error / Warning | A Custom `contentHash` that disagrees with the body-shape hash | None |
| `FUARAN063` | Warning | A blank static href on `Fuaran.link` or `Fuaran.linkSpec` | None |
| `FUARAN064` | Warning | A button whose `Disabled` is a constant-false static binding | None |
| `FUARAN065` | Error | A `Value` hole whose default lies outside its value space | None |
| `FUARAN084` | Warning (Error with `--orchestrated`) | `Binding.Computed` | None |
| `FUARAN900` | Warning | No manifest beside the project; the schema-coupled rules are silent | — |

**Retired build-time codes.** These are no longer emitted by the walker. Each is listed here for one
release so that a consumer matching on it can find where the defect is now reported.

| Code | Was | Now |
|---|---|---|
| `FUARAN040` | A button with `Accessibility = None` and no label | Alias of the tree-time **`FUARAN109`** (an interactive node with no accessible name). The build-time rule matched a field `ButtonSpec` does not have, so it never fired on compiling code |
| `FUARAN041` | A Warning / Critical callout with `Accessibility = None` | **Withdrawn, with no replacement.** It matched a field `CalloutSpec` does not have, so it never fired on compiling code, and no tree-time rule checks the callout live-region opt-out. The gap is open |
| `FUARAN047` | Tab headers and children of different lengths | The tree-time rule of the same code. Only the duplicate build-time emitter is gone |
| `FUARAN048` | Tab tags and children of different lengths | The tree-time rule of the same code |
| `FUARAN049` | `ActiveTag` set with no `TabTags` | The tree-time rule of the same code |

Because the band is shared, a new code is **allocated rather than chosen**:
`pwsh ./scripts/fuaran-codes.ps1 -Next`. The gate runs the same script with `-Check` and fails when
one code names two rules — see [`VOCABULARY.md` §5.1](VOCABULARY.md).

**`FUARAN086` (Error) — a chart names a field its source cannot produce.** Worth a line of its own
here because it is the one whose failure is invisible: a grid column bound to a missing field
renders blank, which a reader notices, while a chart series over a missing field lowers to a
**flat or empty series that looks like data**. `xField`, any `yFields` entry, or an annotation's
field is checked against the schema the chart's own `Transform` pipeline produces, and the message
lists the columns that pipeline actually yields. It stands down wherever that set cannot be closed
(a `$ref` source, a `pivot`) and never fires over a `$state`, `$query` or host `$static` source —
those are the host's to fill and the tree cannot know their columns. Its siblings: `FUARAN087` (the
field is produced but the wrong type), `FUARAN097` (a temporal x-axis over a non-date column), and
`FUARAN114`, the same rule on the read side of a grid.

**Grid columns follow their cell kind (Phase 1909) — `FUARAN077`, `FUARAN114`, `FUARAN163`.** An
**action column** — cell kind `Button` or `ButtonGroup` — draws its own label and hands the whole
row to its handler, so it never displays a field, and it carries **no `field`**. Three consequences:

| Code | Severity | On an action column | Repair |
|---|---|---|---|
| **`FUARAN077`** | Warning | Not raised: a field-less action column is the correct shape, not a blank one. Still raised on a field-less data column. | Give a data column a `field`. |
| **`FUARAN114`** | Error | Not raised: an action column's field is read by nothing, so it is not grounded. | — |
| **`FUARAN163`** | Warning | Raised when an action column declares a `field` — it is never displayed, and sort and export ignore it in every host. | Drop the field. |

A **`TonedPill`** cell's own `field` IS a column reference — the pill's label and its tone key — and
is grounded as a **`FUARAN114` sub-case** (defect case `PillFieldUngrounded`, naming the column):
the same window and the same repair (fix the name, or change the pipeline), so it keeps the code. A
closed schema walk refuses a pill naming a column the pipeline does not produce; an open walk stands
down and the grid grades unchecked. Unrefused, the pill would draw empty, in the default tone, on
every row.

**`FUARAN158`–`FUARAN162` (Error) — the tree is more than this host takes (Phase 1817).** A host
declares an emission budget as a `HostLimits` value and validates with
`PreEmitValidate.validateWithLimits`; each limit it declares that the tree exceeds is refused under
its own code, and the message names the limit, the measured value and the first offending node, so
the repair needs nothing but the message. A host that declares no limits (`HostLimits.unbounded`,
the default) never raises any of them.

| Code | Limit (`HostLimits` field) | Node named | Measured | Repair |
|---|---|---|---|---|
| **`FUARAN158`** | `MaxNodes` — nodes the pre-emit walk visits | the first node past the budget, depth-first pre-order | the tree's node count | emit less — summarise, page a list, split across views |
| **`FUARAN159`** | `MaxDepth` — nesting level, root = 1 | the first node below the limit | the deepest level walked | flatten — drop a wrapper, lift the subtree |
| **`FUARAN160`** | `MaxChildren` — direct children of one container | each over-wide container | that container's child count | group into sub-containers, or use a list / grid |
| **`FUARAN161`** | `MaxGridRows` — a `DataGrid`'s inline rows (`staticRows` or a `$static` source; a bound source is never judged) | each over-long grid | that grid's inline row count | trim, or bind the grid to a source the host pages |
| **`FUARAN162`** | `MaxSerializedBytes` — UTF-8 size of the canonical JSON | the root | the encoded size | emit less, or move large inline data behind a bound source |

**Ask before you emit.** The active limits are in the AI-tools capability report
(`Fuaran.UI.AiTools.Capabilities.emissionLimits`): the declaration's name and, per declared limit,
its value and the code a breach of it is refused with. An empty `limits` list means the host states
no limit — not a limit of zero. The named presets `HostLimits.email`, `HostLimits.mobile` and
`HostLimits.card` are **starting points, not authority**: each figure's source (a vendor's documented
ceiling, or a stated judgement) is written beside it in `src/Fuaran.UI/HostLimits.fs`, and a host
that knows its own budget declares that instead.

**Decode guards and emission limits are different protections, and neither substitutes for the
other.** The decode guards (`WireLimits`, `WIRE_FORMAT.md` §21; the decode-time kind policy) protect
the reader from a **hostile** document: they are the same for every conformant host, and a payload
past them is refused whatever its author meant — FUARAN091 is the pre-emit mirror of the depth one.
Emission limits protect the reader from an **honest** document that is simply more than this
particular host renders well; a tree refused under them is still a valid wire document. Declaring
generous emission limits does not loosen any decode guard, and declaring none leaves a host exactly
as protected against hostile input as it was.

**Suppressing a validator finding.** Source that deliberately holds a rejected shape — canonically a negative test asserting the runtime reports the defect — opts out with a comment pragma: `// fuaran-validator: disable FUARAN047, FUARAN048 — reason` (file-scoped) or `// fuaran-validator: disable-next-line FUARAN044` (the following line only). Suppressed findings are counted in the run summary rather than hidden, and only the reporting layer is filtered — every check still runs. This is a **host-side** mechanism on the .NET validator, not part of the cross-implementation spec surface. See the [validator README](../src/Fuaran.UI.Validator/README.md#suppressing-a-finding).

**Reserved band – `FUARAN2xx` = host/pack-assigned.** Codes in the `FUARAN200`–`FUARAN299` band are **reserved for rules a host or a rule-pack contributes**, layered atop the spec's own families. The spec will never mint a `FUARAN2xx` code, so a pack can assign in this band without colliding with a future spec rule. This is the concrete, per-domain expression of `Fuaran.Core.Validator`'s pack-provenance convention (a pack rule's family id is `pack + "/" + ruleId`, so the contributing pack is recoverable from any finding). A host that surfaces both spec and pack findings can therefore partition them by band (`0xx` = spec, `2xx` = pack/host) and attribute each pack finding by its `/`-delimited family id – pack-layering stays legible and certifiable through the public validator framework without the framework shipping any pack content.

---

## 5. Pre-emit validator codes – the full table

Every code `Fuaran.UI.PreEmitValidate.describe` can mint, with its severity and the exact shape of
each message it renders. The section below is **generated** from `PreEmitValidate.DefectCodes.table`
– the one place the validator writes a code, a severity or a message – and a test in
`Fuaran.UI.Tests` fails when this document and the table disagree. Do not edit it by hand: change
the table, then regenerate with `FUARAN_REGEN_ERROR_CODES=1` set while running that suite.

Each message is shown as its template: `{nodeId}`, `{key}` and the other braced names are the
values the defect carries. A code with more than one message shape (a sub-case, or a flag that
changes the wording) lists one template per variant, under the defect case that raises it
(`describeVariant` returns the variant for a defect in hand). The note beside a variant names the
roadmap phases that introduced or widened the rule.

<!-- BEGIN GENERATED: PreEmitValidate.DefectCodes.toMarkdown — do not edit by hand -->

#### `FUARAN-DUP-ID` — Error

`DuplicateNodeId`

```text
node id '{nodeId}' appears {count} times
```

#### `FUARAN-EMPTY-CUSTOM` — Error

`EmptyCustomKindIdentifier`

```text
Custom node has empty moduleId='{moduleId}' / componentId='{componentId}'
```

#### `FUARAN-EMPTY-ID` — Error

`EmptyNodeId`

```text
a node carries an empty id
```

#### `FUARAN047` — Error

`TabHeaderCountMismatch`

```text
tabs '{nodeId}' declares {headerCount} headers but {childrenCount} children — the renderer aligns headers 1:1 with children by index
```

#### `FUARAN048` — Error

`TabTagCountMismatch`

```text
tabs '{nodeId}' declares {tagCount} tags but {childrenCount} children — the tag → index round-trip needs parity
```

#### `FUARAN049` — Warning

`TabActiveTagWithoutTags`

```text
tabs '{nodeId}' sets ActiveTag but TabTags = None — the tag binding has nothing to resolve against
```

#### `FUARAN068` — Error

`CustomPropSchemaViolation`

```text
custom node '{nodeId}' ({moduleId}/{componentId}) violates its declared prop schema — {propDefectCount} prop defect(s)
```

#### `FUARAN069` — Warning

`InertControl` (Phases 423, 426)

```text
{control} on '{nodeId}' has no event handler and no writable value binding — bind its value to $state.<key> / $filters.<name>, or supply the handler (Phase 426 write-back default)
```

#### `FUARAN070` — Error

`DanglingSelection` (Phase 427)

```text
'{readerNodeId}' reads Binding.Selection on '{target}' but no node with that id exists — point the binding at the selection-producing node's id
```

#### `FUARAN071` — Warning

`SelectionOverNonProducer` (Phase 427)

```text
'{readerNodeId}' reads Binding.Selection on '{target}', which is not a selection-producing (Visualisation) node — nothing in the tree will write that selection
```

#### `FUARAN072` — Warning

`OrphanQueryFetch` (Phase 428)

```text
'{readerNodeId}' calls into Query '{queryName}' but no Binding.Query in the tree reads that slot — an orphan fetch (name typo?)
```

#### `FUARAN073` — Warning

`CallResultDropped` (Phase 428)

```text
'{readerNodeId}' calls '{endpoint}' with neither an onResult closure nor an into target — the response is dropped (fine for a command endpoint; add into for data)
```

#### `FUARAN074` — Warning

`DecorativeFilter` (Phases 421, 424)

```text
filter '{name}' (declared on '{declaringNodeId}') is consumed by nothing — no Binding.Filter read, Query.dependsOn, or Transform param references it
```

#### `FUARAN075` — Error

`DanglingFilterReference` (Phases 421, 424, 862, 1892)

```text
'{readerNodeId}' declares a filter edge on '{name}' (dependsOn / Transform param source) but no Filters chip declares that name
```

#### `FUARAN076` — Warning

`UnreferencedTransformParam` (Phases 421, 424)

```text
'{readerNodeId}' declares Transform param '{name}' but the pipeline never references it (paramsOf)
```

#### `FUARAN077` — Warning

`BlankGridColumn` (Phase 425)

```text
grid '{nodeId}' column '{columnLabel}' has neither a value closure nor a field — it renders blank
```

#### `FUARAN078` — Warning

`UnstableRowIdentity` (Phases 425, 427)

```text
grid '{nodeId}' has neither rowKey nor rowKeyField — no stable row identity
```

#### `FUARAN082` — Error

`DuplicateSwitchMatch` (Phase 392)

```text
Switch '{nodeId}' has two or more cases matching '{matchValue}' — first-match-wins makes the later case dead; give each case a distinct match value (Phase 392)
```

#### `FUARAN083` — Warning

`UngroundedSwitchStateKey` (Phases 12, 392, 768)

```text
Switch '{nodeId}' has an empty stateKey — it can never resolve a case and is stuck on its default; name the state key the switch selects on (Phase 392)
```

#### `FUARAN085` — Warning

`DuplicateWriteBackKey` (Phase 596)

```text
state key '{stateKey}' has {writerCount} handler-free write-back writers ({writers}) — typing in one silently overwrites the other's captured value; give each field its own key
```

#### `FUARAN086` — Error

`ChartFieldUngrounded` (Phases 640, 1486)

```text
chart '{nodeId}' references field '{field}' absent from the schema its own source PRODUCES [{schemaColumns}] — it would lower silently flat/empty; fix the name, or change the pipeline so it produces the column (Phase 640/1486)
```

#### `FUARAN087` — Error

`ChartFieldTypeMismatch` (Phase 640)

```text
chart '{nodeId}' plots field '{field}' of type '{columnType}' — the lowering reads non-numeric cells as 0.0, a silently flat series (Phase 640)
```

#### `FUARAN088` — Error

`ChartPieSeriesShape` (Phases 638, 640)

```text
pie chart '{nodeId}' declares {seriesCount} series — the pie lowering refuses anything but exactly one (no silent truncation; Phase 638/640)
```

#### `FUARAN089` — Warning

`ChartStackedMeaningless` (Phases 637, 640)

```text
chart '{nodeId}' sets Stacked=true on kind {kind} — the lowering ignores it (dead intent; Phase 637/640)
```

#### `FUARAN090` — Warning

`InertEditableGrid` (Phase 663)

```text
grid '{nodeId}' sets editable=true but its source is not a direct $state binding — edits have nowhere to go, every cell renders read-only; source the grid (and any chart that should track edits) from a shared {"$type":"State","key":…,"default":[rows]} binding
```

#### `FUARAN091` — Error

`MaxDepthExceeded` (Phase 781)

```text
node '{nodeId}' nests deeper than the wire limit MaxDepth = {limit} (WIRE_FORMAT §21) — the tree was not walked past this point; flatten the nesting
```

#### `FUARAN092` — Warning

`ProtectedNonMailtoLink` (Phase 812)

```text
link '{nodeId}' sets protection="email" on a non-mailto href — the Email strategy only protects a mailto: address, so the renderers ignore the flag (dead intent); drop the protection or point the href at mailto:<address>
```

#### `FUARAN093` — Error

`PageSizeWithoutPageKey` (Phase 862)

```text
grid '{nodeId}' declares pageSize but no pageStateKey — nothing carries the page position, so the grid renders every row and the page size is dead intent; add pageStateKey naming the State key the pager writes {"page":N} to
```

#### `FUARAN094` — Error

`UnhonourableSort.NoSortStateKey` (Phases 861, 863)

```text
grid '{nodeId}' column '{label}' declares sortable=true but the grid names no sortStateKey — a column narrows a behaviour, it cannot turn one on; add sortStateKey to the grid or drop the column flag
```

`UnhonourableSort.ColumnHasNoField` (Phases 861, 863)

```text
grid '{nodeId}' column '{label}' declares sortable=true but has no field — nothing names the row property to order by; add field, or drop the flag and let the column render unsorted
```

`UnhonourableSort.DefaultSortColumnOutOfRange` (Phases 861, 863)

```text
grid '{nodeId}' declares defaultSort on column {column} but the grid has {count} column(s) — the declared order can never be applied; point it at an existing column index
```

#### `FUARAN095` — Error

`UneditableColumnDeclared.GridNotEditable` (Phase 863)

```text
grid '{nodeId}' column '{columnLabel}' declares editable=true but the grid is not editable — a column narrows a behaviour, it cannot turn one on; set editable on the grid, or drop the column flag
```

`UneditableColumnDeclared.NoReachableDestination` (Phase 863)

```text
grid '{nodeId}' column '{columnLabel}' is editable but no destination is reachable — declare editStateKey, or source the grid from a direct {"$type":"State","key":…} binding so the edit has somewhere to commit
```

#### `FUARAN096` — Warning

`DoublePagedGrid` (Phase 862)

```text
grid '{nodeId}' pages client-side on pageStateKey '{pageStateKey}' while its source is a query depending on that same key — the host already returns the page, so slicing it again would page the page; drop pageSize to let the host page, or drop the dependsOn to page client-side
```

#### `FUARAN097` — Error

`ChartTemporalXNotDate` (Phases 882, 1486)

```text
chart '{nodeId}' declares a temporal x-axis over field '{field}' of type '{columnType}' — a date axis needs a date column, and every row's x would read as 1970-01-01; give the column type 'date' (canonical ISO-8601 YYYY-MM-DD cells), or drop xScale to plot the values as categories (Phase 882)
```

#### `FUARAN098` — Warning

`SetStateNoReader` (Phases 782, 860, 866, 932)

```text
'{nodeId}' writes state key '{key}' but nothing in the tree reads it — the gesture runs and the user sees no change (a fake affordance); bind a reader to {"$type":"State","key":"{key}"}, select a Switch on it, or name it as a grid's sortStateKey/pageStateKey. If the key is written for the HOST to read, this warning is expected and can be ignored (Phase 932)
```

#### `FUARAN099` — Error

`CompareKeyUnreachable` (Phases 782, 864)

```text
form '{nodeId}' field '{fieldId}' compares against state key '{key}', but no field in the form owns that key and nothing in the tree writes it — the predicate can never be met or unmet, only absent, so the field reads as constrained and is not; point 'against' at a sibling field's id (a form field's value lives in State under its own id), or give the key a writer
```

#### `FUARAN100` — Warning

`RuleSlotUnhonourable` (Phase 864)

```text
form '{nodeId}' field '{fieldId}' declares {slot} on a {control} control, which cannot honour it — the constraint is carried and never applied (dead intent); move the rule to a text control, or drop the slot. If a host you target DOES honour it, this warning is expected and can be ignored
```

#### `FUARAN101` — Warning

`CompareDuplicatesBound` (Phase 864)

```text
form '{nodeId}' field '{fieldId}' compares against a LITERAL while its control already declares {bound} — two sources for one bound, free to disagree, and nothing decides which wins; drop the compare and keep the control's bound, or make the operand read something that changes ({"$type":"State","key":"<sibling field id>"}), which is what the rule slot is for
```

#### `FUARAN102` — Warning

`DateLiteralWhereNowPlausible` (Phase 765)

```text
'{nodeId}' names the current instant and states a hardcoded date: "{literal}" — the value was true when it was written and is wrong from the next day onward; bind the slot to {"$type":"Now"} (with a Format binding for the display shape) so the host furnishes the instant. If the date is genuinely historical, reword the label so it does not read as the present
```

#### `FUARAN103` — Warning

`SwitchKeyNoWriter` (Phases 768, 782, 1122, 1646)

```text
switch '{nodeId}' selects on state key '{key}' but nothing in the tree can write it — one branch renders forever; give the key a writer (an Action.SetState on a button, a Call with into: {"$type":"State","key":"{key}"}, or a control write-back slot bound to it), or select on the binding that already changes (a Selection, a Filter, a Query). If the key is written by the HOST, this warning is expected and can be ignored
```

#### `FUARAN104` — Warning

`KindNotAdmitted` (Phase 1020)

```text
node '{nodeId}' is a '{kind}', which decode policy '{policy}' does not admit
```

#### `FUARAN105` — Warning

`TransformSourceInert` (Phases 782, 865, 1075, 1665)

```text
'{nodeId}' derives from a Transform over state key '{key}', but NOTHING in the tree seeds that key — no reader declares a defaultValue for it and nothing writes it — so the pipeline runs over an EMPTY table and renders a plausible wrong answer (a count of zero) that nothing reports; declare the rows once on any reader of the key ({"$type":"State","key":"{key}","defaultValue":[…]}), which seeds the slot for every reader including this one, or give the key a writer. If the key is populated by the HOST, this warning is expected and can be ignored
```

#### `FUARAN106` — Error

`ConflictingStateSeeds` (Phases 782, 1075)

```text
state key '{key}' is seeded twice with DIFFERENT values — '{firstNodeId}' and '{secondNodeId}' each declare a defaultValue for it, and a key has one slot, so only the first declaration ('{firstNodeId}') takes effect and the second is silently discarded; declare the value ONCE and let the other reader carry {"$type":"State","key":"{key}"} with no defaultValue, or give the two readers different keys if they are genuinely different data
```

#### `FUARAN107` — Warning

`DuplicateInlineTable` (Phase 1075)

```text
'{firstNodeId}' and '{secondNodeId}' each carry their own inline copy of the SAME table — the two copies can silently diverge, and nothing in the tree says they are meant to be one source; declare the rows once under a state key ({seedKeyState}) and have the other read {"$type":"State","key":"<key>"} with no defaultValue, which resolves to the seeded slot. If the two are genuinely independent data that happen to match, this warning is expected and can be ignored
```

#### `FUARAN108` — Error

`MediaWithoutLabel` (Phase 1076)

```text
media node '{nodeId}' has an EMPTY label — a media element is a transport, not a picture, so it is never decorative and there is no honest empty case the way there is for an image's alt; without a name it is announced to a screen reader as "video" or "audio" and nothing more, telling the reader that a player exists and not what it plays. Give 'label' the text a listener needs to decide whether to play it
```

#### `FUARAN109` — Warning

`InteractiveWithoutAccessibleName` (Phase 727)

```text
{kind} '{nodeId}' reaches a screen reader with no name — '{slot}' is empty and the node declares neither accessibility.label nor accessibility.labelledBy, so its accessible name would have to come from its text content and there is none; give '{slot}' the text a listener needs, or name the element with accessibility.label
```

#### `FUARAN110` — Warning

`DanglingAccessibilityReference` (Phase 727)

```text
node '{nodeId}' declares accessibility.{slot} = '{target}', which is not a node in this tree — the emitted {attribute} points at nothing and the browser ignores it, so the element is announced as though the reference had never been written; point it at a node that exists, or drop the slot and name the element with accessibility.label
```

#### `FUARAN111` — Warning

`EmptyAccessibilityDeclaration` (Phase 727)

```text
node '{nodeId}' declares accessibility.{slot} and leaves it EMPTY — a declared name that names nothing, which the renderer drops rather than emits, and which additionally silences the missing-name check that would otherwise have caught this node; give the slot real text, or remove it so the element's own content supplies the name
```

#### `FUARAN112` — Warning

`WireLossyActionClosure` (Phase 577)

```text
node '{nodeId}' carries a host closure in '{slot}' — the canonical encoder drops the payload and the decoder rebuilds it as "<closure>", so a decoding host receives an affordance that fires and does nothing; replace it with a wire-representable action (Action.Notify, or Action.Call with into:) and bind the typed behaviour host-side to the artifact's declared action hole. Encode with encodeNodeForTransport to have this refused rather than warned. If this tree is rendered IN PROCESS and never serialised, the closure is correct and this warning is expected
```

#### `FUARAN113` — Error

`TrackWithoutLabel` (Phase 1110)

```text
media node '{nodeId}' carries a text track at index {trackIndex} with an EMPTY label - a track's label IS its entry in the user agent's track menu, and it is the only thing that tells one track from another there, so an unlabelled one is offered as its kind alone and a reader choosing between two captions tracks is shown two identical choices. Give the track's 'label' the text a reader needs to pick it
```

#### `FUARAN114` — Error

`GridFieldUngrounded` (Phases 1149, 1486, 1909)

```text
grid '{nodeId}' names field '{field}', absent from the schema its own source PRODUCES [{schemaColumns}] — everything that reads the name resolves it against nothing: a cell whose kind displays the column's field shows an empty value, sort and export read an empty key, and for rowKeyField every row shares one empty key and row identity collapses; fix the name, or change the pipeline so it produces the column (Phase 1149/1486)
```

`PillFieldUngrounded` (Phases 1149, 1909)

```text
grid '{nodeId}' column '{columnLabel}' has a TonedPill cell naming field '{field}', absent from the schema its own source PRODUCES [{schemaColumns}] — the pill's label and its tone key both resolve against nothing, so every row draws an empty pill in the default tone; fix the name, or change the pipeline so it produces the column (Phase 1909)
```

#### `FUARAN115` — Error

`EmbedWithoutTitle` (Phase 1111)

```text
embed node '{nodeId}' has an EMPTY title — a frame is a focus container a reader tabs into, not a picture, so it is never decorative; without a name it is announced to a screen reader as "frame" and nothing more, telling the reader that something is embedded and not what. Give 'title' the text a reader needs to decide whether to enter it
```

#### `FUARAN116` — Warning

`EmbedSandboxWeakened` (Phase 1111)

```text
embed node '{nodeId}' declares both AllowScripts and AllowSameOrigin — against a SAME-ORIGIN document that pair is the documented sandbox escape, because the framed document can then reach its own frame element and remove the sandbox attribute. It is also what every real cross-origin embed needs, and nothing in this tree says which this is, so this is a warning rather than a refusal: confirm the source is a third-party origin, or drop AllowSameOrigin if the provider does not need its own storage
```

#### `FUARAN118` — Warning

`EmptyTooltipDeclaration` (Phase 1112)

```text
node '{nodeId}' declares a tooltip and leaves it EMPTY — a hint that hints nothing. The renderers emit no hint element for an empty one, so the markup you expected is silently absent and so is the aria-describedby that would have carried it to a screen reader; write the sentence the reader needs, or drop the slot
```

#### `FUARAN119` — Warning

`TooltipOnHiddenNode` (Phase 1112)

```text
node '{nodeId}' carries a tooltip while declaring accessibility.hidden = true — aria-hidden removes the node and its whole subtree from the accessibility tree, taking the hint and its aria-describedby with it, so what is left is a hover affordance for sighted pointer users on a node declared not to be part of the interface. Drop the hint, or drop the hidden declaration if the node was meant to be announced
```

#### `FUARAN120` — Warning

`ComboboxWithoutOptions` (Phase 1113)

```text
combobox '{fieldId}' on node '{nodeId}' declares a STATIC and EMPTY option list — a typeahead with nothing to suggest, and no dynamic source that could supply anything later. It renders, it takes focus, and it opens no listbox: with allowFreeText it is a plain text input you did not ask for, and without it no value is admissible at all. Give the options a Query / State source if the suggestions arrive at runtime, list them if they are known, or use a Text field if free text is what you meant
```

#### `FUARAN121` — Warning

`UploadGestureWithoutHandler` (Phase 1115)

```text
file upload '{nodeId}' declares {gestures} and carries no onSelect handler — the gesture is invited and consumes nothing. A picker at least leaves the chosen filename in the user agent's own chrome; a dropped or pasted file disappears on release with no feedback at all, so the reader is told the upload worked and it did not. Wire onSelect, or drop the gesture declaration until it is wired
```

#### `FUARAN122` — Warning

`PopoverWithoutAnchor.NoAnchor` (Phase 1119)

```text
popover '{nodeId}' declares no anchor — a Popover is positioned against the node it was opened from, and with nothing to position against the renderer leaves it in the document flow wherever the node happens to sit, which is the static floor and not the surface you asked for. Set anchor to the id of the control that opens it, or use modality Modal if a blocking dialog is what you meant
```

`PopoverWithoutAnchor.DanglingAnchor` (Phase 1119)

```text
popover '{nodeId}' declares anchor = '{target}', which is not a node in this tree — the anchor resolves to no element, so the popover is left in the document flow exactly as an undeclared one is, and the declaration reads as honoured when it was not. Point it at a node that exists (a dangling anchor is usually a typo or a node that has since moved), or drop the declaration and use modality Modal if a blocking dialog is what you meant
```

#### `FUARAN123` — Warning

`AnchorOnBlockingModal` (Phase 1119)

```text
modal '{nodeId}' declares anchor = '{anchor}' while its modality is Modal — a dead declaration. A blocking dialog is positioned by its scrim and not by an element, so the id rides the wire, survives every round trip and changes nothing on any host. Set modality to Popover if an anchored surface is what you meant, or drop the anchor
```

#### `FUARAN124` — Warning

`DirectionOnTextlessNode` (Phase 1472)

```text
node '{nodeId}' declares style.direction while its kind is {kind} — a dead declaration. A direction states which way a run of text reads and isolates it from the bidirectional context around it, and this kind lays out no text and holds no children to inherit it, so the declaration rides the wire, survives every round trip and changes nothing on any host. Move it to the node that carries the text, or drop it
```

#### `FUARAN125` — Warning

`DeadPrintBreak.RepeatHeaderNoHeader` (Phase 1473)

```text
grid '{nodeId}' declares repeatHeader while it renders no header cells — a dead declaration. Repeating a header at the top of every page needs a header row group with something in it, so this rides the wire, survives every round trip and changes nothing on any host. Give the grid its columns (or, on the static leg, its headers), or drop the declaration
```

`DeadPrintBreak.NoSubtreeToKeepTogether` (Phase 1473)

```text
node '{nodeId}' declares keepTogether while it renders no subtree — a dead declaration. Keeping a subtree whole across a page boundary needs a subtree that could straddle one, and this container has no rendered children, so the declaration rides the wire, survives every round trip and changes nothing on any host. Move it to the container that holds the content, or drop it
```

#### `FUARAN126` — Error

`TreeItemIdDuplicated` (Phase 1120)

```text
tree '{nodeId}' carries more than one row with the id '{itemId}' — a row id is what the expanded set and the selection NAME, so a repeated one makes both ambiguous: expanding one row opens two, and a restored selection lands on whichever the host reached first. Give every row in this tree its own id
```

#### `FUARAN127` — Error

`TreeItemWithoutLabel` (Phase 1120)

```text
tree '{nodeId}' carries a row '{itemId}' with an EMPTY label — a row's label is the only thing a reader walking the hierarchy has, so an unnamed one is announced as its level and its position and nothing else. Give the row's 'label' the text a reader needs to decide whether to open it
```

#### `FUARAN128` — Warning

`DeadAutoAdvance.NoWritableSelector` (Phase 1122)

```text
switch '{nodeId}' declares autoAdvanceMs while it selects on a binding that is not a state key — a dead declaration. Timed advance moves the switch's OWN key, and a switch driven by a selection, a filter or a query has no key of its own to move, so the interval rides the wire, survives every round trip and advances nothing on any host. Select on a state key, or drop the interval
```

`DeadAutoAdvance.NotEnoughCases` (Phase 1122)

```text
switch '{nodeId}' declares autoAdvanceMs while it carries fewer than two cases — a dead declaration. There is nowhere for a tick to advance to but the case already showing, so the interval would rewrite the key with the value it already holds. Give the switch the cases it cycles through, or drop the interval
```

#### `FUARAN129` — Warning

`DeadTransferPairing.NoSource` (Phase 1123)

```text
grid '{nodeId}' accepts transfers on the key '{key}' and no grid in this tree releases to it — a dead drop zone. The grid draws its place control and its rows accept drops, so a reader is invited to move something into it, and no drag that could satisfy the invitation can ever begin. Declare transferOutKey '{key}' on the grid rows should come FROM, or drop transferInKey
```

`DeadTransferPairing.NoTarget` (Phase 1123)

```text
grid '{nodeId}' releases transfers to the key '{key}' and no grid in this tree accepts from it — a drag handle with nowhere to go. A reader can lift a row and will find no list that will take it. Declare transferInKey '{key}' on the grid rows should go TO, or drop transferOutKey
```

#### `FUARAN130` — Warning

`TransferWithoutRowIdentity` (Phase 1123)

```text
grid '{nodeId}' declares a cross-container transfer with no rowKeyField — the transfer record names the moved row by its identity, and a rowKey closure crosses the wire as the closure placeholder, so a decoded transfer out of this grid would report that nothing moved. Name the column that identifies a row with rowKeyField
```

#### `FUARAN131` — Warning

`DeadExportAffordance.NoRowSource` (Phase 1125)

```text
grid '{nodeId}' declares exportable while it names no row source — a dead control. The reader is offered a download of the rows this grid holds, and it holds none and can never be given any on this binding, so the file would be a header record and nothing else. Give the grid a source (or staticRows), or drop the declaration
```

`DeadExportAffordance.NoColumns` (Phase 1125)

```text
grid '{nodeId}' declares exportable while it declares no columns — a dead control. The columns are the exported file's fields, so with none the header record is empty and so is every row record, whatever the source resolves to. Give the grid its columns, or drop the declaration
```

#### `FUARAN132` — Warning

`RatingValueOutOfScale` (Phase 1130)

```text
rating '{fieldId}' on node '{nodeId}' declares the static value {value} on a scale of 0 to {max} — a figure the control cannot show. The renderer clamps into the scale and announces the clamped figure, so the reader is told something the document did not say. Correct the value, or raise max if the larger scale is what you meant
```

#### `FUARAN133` — Error

`ColorValueNotHex` (Phase 1130)

```text
colour field '{fieldId}' on node '{nodeId}' declares the static value '{value}', which is not the canonical #rrggbb hex form. This is the one shape a native colour input can hold, and it is what the decoder accepts: a tree carrying anything else encodes to a document no conformant host will read back, including this one. Write the six-digit form (#ff8800), or bind the value if it arrives at runtime
```

#### `FUARAN134` — Warning

`CaptureAcceptMismatch` (Phases 1116, 1130)

```text
file upload '{nodeId}' asks for the {device} but its accept list ({accept}) does not select that device. The capture keyword asks the platform for a recording device; which one it opens is decided by accept, so this document opens whichever the user agent guesses and the reader is handed a device you did not name. Add the device's own media type to accept (image/* or video/* for the camera, audio/* for the microphone), or drop the capture declaration if the ordinary file picker was what you meant
```

#### `FUARAN135` — Warning

`TokensAdmitsNothing` (Phase 1121)

```text
token field '{fieldId}' on node '{nodeId}' admits no free text and declares a STATIC and EMPTY suggestion list — no token can ever be put into it, by any gesture. It renders as an empty chip row beside an entry box that refuses every keystroke. Give the suggestions a Query / State source if they arrive at runtime, list them if they are known, or leave allowFreeText at its default of true if open tokens are what you meant
```

#### `FUARAN136` — Warning

`TokensStaticDuplicate` (Phase 1121)

```text
token field '{fieldId}' on node '{nodeId}' declares the static token '{token}' more than once. A token list is a set the reader sees as chips, and two identical chips are one fact drawn twice with two remove buttons that do different things. The renderer refuses a duplicate at the moment of adding and the server-side submission floor refuses it on arrival; remove the repeat here
```

#### `FUARAN137` — Error

`ChartAnnotationNonFinite` (Phases 1490, 1492)

```text
chart '{nodeId}' {subject} carries the value {value} — an annotation addresses a place on the value axis, and NaN / Infinity names none; it would also enter the axis domain and take every gridline, tick and mark to NaN with it. Give a finite value in the axis's own units, or drop the annotation (Phase 1490)
```

#### `FUARAN138` — Error

`ChartAnnotationKeyUngrounded.Absent` (Phases 1491, 1492)

```text
chart '{nodeId}' {subject} addresses the category '{key}', which none of the rows carries — a band axis's domain IS the set of keys in its rows, so a key outside that set names no band to draw at. Use a key the x column carries, or declare xScale 'Temporal' and address a date (Phase 1491)
```

`ChartAnnotationKeyUngrounded.Repeated` (Phases 1491, 1492)

```text
chart '{nodeId}' {subject} addresses the category '{key}', which {occurrences} rows carry — an annotation is placed from the band's own extent, and a duplicated key has two, so which one it lands on would depend on traversal order rather than on the data. Aggregate the rows to one per key, or address a key that appears once (Phase 1491)
```

#### `FUARAN139` — Error

`ChartAnnotationAxisMismatch` (Phases 1491, 1492)

```text
chart '{nodeId}' {subject} carries a {addressForm} address on a {axisForm} x axis — an annotation addresses the axis in the axis's own form, and the language refuses the mismatch rather than coercing it (a date read as a category grounds against no band; a category read as a date lands on 1970-01-01). Give the address in the axis's form, or change the axis (Phase 1491)
```

#### `FUARAN140` — Error

`ChartAnnotationDateUnparseable` (Phases 1491, 1492)

```text
chart '{nodeId}' {subject} carries the date '{iso}', which is not a readable ISO-8601 day — a temporal address enters the axis extent before the ticks are chosen, so an unreadable one would be placed at 1970-01-01 and drag the whole axis back with it. Give a canonical YYYY-MM-DD date naming a real calendar day (Phase 1491)
```

#### `FUARAN141` — Error

`ChartAnnotationRangeUnordered` (Phase 1492)

```text
chart '{nodeId}' {subject} runs from '{fromText}' to '{toText}', which is backwards on the axis it addresses — a band names an interval, and the ends are not interchangeable. Swap them; the language will not, because a pair written backwards is a mistake about the data and drawing the band you did not describe would carry it through to the reader (Phase 1492)
```

#### `FUARAN142` — Warning

`UnsafeUrlScheme` (Phase 1523)

```text
node '{nodeId}' declares a {slot} the renderer floor refuses: {reason}. Every conformant host refuses this before it reaches the document, so the slot renders as a refusal marker rather than as the destination you wrote. If the intent was to run something on click, the wire has typed actions for it (Action.Notify, Action.Call, Action.SetState); if the destination is real, name its scheme (http / https / mailto / tel / ftp / sftp) or write a same-origin relative path
```

#### `FUARAN143` — Warning

`UnsafeCssValue` (Phase 1523)

```text
node '{nodeId}' declares '{value}' in its {slot} slot, which carries a character or function that lets a CSS value leave its own declaration (a semicolon, a brace, a backslash, a control byte, `url(`, `expression(`). Every renderer emits an empty value here instead, because the same string in a style attribute is a second declaration the document never wrote - and `url(` is a network request made at render time with no user act. Write a single CSS value with none of those
```

#### `FUARAN144` — Warning

`MalformedTrackList` (Phase 1523)

```text
grid '{nodeId}' declares templateColumns '{value}', which is not shaped like a CSS track-list. It is safe - the renderers emit it - but no browser reads it as a column definition, so the grid falls back to one column. Write track sizes (1fr 2fr auto), a repeat(...), or a minmax(...)
```

#### `FUARAN145` — Warning

`UnsafePaintValue` (Phase 1523)

```text
node '{nodeId}' declares '{value}' as its {slot} paint, which is not a colour. The renderers emit `none` instead: an SVG paint slot accepts `url(...)` as a paint-server reference, which is also how a remote fetch is spelled, so the slot admits only hex (#rgb / #rrggbb / #rrggbbaa), a bare ident (every named colour and keyword - red, steelblue, currentColor, none, transparent, inherit), and the colour functions (rgb, rgba, hsl, hsla, oklch, oklab, lch, lab, color)
```

#### `FUARAN146` — Warning

`UnsupportedLinkAnchor` (Phase 1523)

```text
link '{nodeId}' declares {slot}='{value}', which every renderer drops. `target` is closed to `_self` and `_blank` - `_parent` / `_top` navigate a document that framed this one, and a named frame addresses a browsing context this document did not create. `rel` is closed to the descriptive tokens, and `opener` in particular is refused: it re-enables window.opener on a `_blank` link, handing the opened page a live reference to this one. A `_blank` link is emitted with `noopener noreferrer` whether or not it asks
```

#### `FUARAN147` — Error

`SwitchCaseSelectorShape.BothPresent` (Phase 1535)

```text
Switch '{nodeId}' case {caseIndex} carries both 'match' and 'when' — exactly one selects a case; 'match' compares the switch's `on` selector against a literal, 'when' evaluates a Binding<bool> and needs no selector (Phase 1535)
```

`SwitchCaseSelectorShape.NeitherPresent` (Phase 1535)

```text
Switch '{nodeId}' case {caseIndex} carries neither 'match' nor 'when' — a case that names no condition can never be selected; give it a literal 'match' against the switch's `on` selector, or a 'when' Binding<bool> predicate (Phase 1535)
```

#### `FUARAN148` — Warning

`VisibleStateNoWriter` (Phases 782, 1535)

```text
node '{nodeId}' is visible only while state key '{key}' is true, and nothing in this tree writes it — a default-less State binding resolves to false at a bool slot, so the node is removed with nothing saying why; declare the default (true = visible unless something says otherwise) or add the writer (Phase 1535)
```

#### `FUARAN149` — Warning

`ReservedStateKeyWrite.Declared` (Phases 782, 1550)

```text
'{nodeId}' writes state key '{key}', which the host has reserved — every tree-originated write to it is refused at dispatch, so the gesture runs and nothing happens; write a key the host has not closed, or ask the host to expose the slot through a Query or a Call (Phase 1550)
```

`ReservedStateKeyWrite.Undeclared` (Phases 782, 1550)

```text
'{nodeId}' writes state key '{key}', which is not host-reserved and which nothing in this tree reads — the shape of a host-owned slot a rendered tree can reach. If the key is the HOST's, declare it (StateStore.declareReserved) and this write is refused instead of silently landing in the host's slot; if it is the tree's own, FUARAN098 beside this says what is missing (Phase 1550)
```

#### `FUARAN152` — Error

`SkeletonRowsOutOfRange` (Phase 1666)

```text
Skeleton '{nodeId}' declares rows = {rows}, outside 0 … {maxRows} (WIRE_FORMAT §21.9) — a negative count draws nothing, and a count above the bound names more placeholder rows than any conformant host may carry, so the encoded tree would be refused on decode; pick a count in range
```

#### `FUARAN153` — Warning

`BadgeToneContradictsLabel` (Phase 1734)

```text
Badge '{nodeId}' reads "{label}" and is toned {variant} — the label and the tone name two different severities, and the tone is what a reader scanning the page acts on first. Tone it {label} to agree with the word, or reword the label to the severity you meant. If the badge is deliberately untoned, Neutral and Brand carry no severity claim and this rule is silent on them
```

#### `FUARAN154` — Warning

`PillToneContradictsValue` (Phase 1734)

```text
a TonedPill column on '{nodeId}' paints the value "{value}" in the {tone} tone — the value names one severity and the tone names another, so every row carrying it is coloured against what it says. Map "{value}" to the {value} tone, or drop the entry and let the column's default carry it
```

#### `FUARAN155` — Error

`UnstyledDateFormat` (Phase 1810)

```text
'{nodeId}' formats an instant with a Date format that declares neither dateStyle nor timeStyle, so nothing says what the reader is shown. Declare dateStyle for a date, timeStyle for a time of day, or both for a date-time
```

#### `FUARAN156` — Error

`FallbackRepeatsKind` (Phase 1812)

```text
'{nodeId}' declares a fallback that itself contains a {kind} — the kind it stands in for. A reader that needs the fallback is one that cannot read {kind}, so this fallback would be a placeholder too. Build the fallback from kinds every reader has (a Markdown or a Box of them)
```

#### `FUARAN157` — Error

`NestedFallback` (Phase 1812)

```text
'{nodeId}' declares a fallback in which '{innerId}' declares a fallback of its own. A behind reader lifts one fallback — the one on the node it cannot read — and never consults a fallback's fallback, so the inner one has no reader. Remove it, or make the inner node plain
```

#### `FUARAN158` — Error

`HostNodeCountExceeded` (Phase 1817)

```text
the tree carries {measured} nodes, over the {limit} this host allows (maxNodes, host limits '{limits}'); '{nodeId}' is the first node past the budget. Emit less: summarise, page a list, or split the content across views
```

#### `FUARAN159` — Error

`HostDepthExceeded` (Phase 1817)

```text
'{nodeId}' is the first node below nesting level {limit}, the deepest this host allows (maxDepth, host limits '{limits}'); the tree reaches level {measured}. Flatten the nesting — remove a wrapper box, or lift the subtree a level
```

#### `FUARAN160` — Error

`HostChildrenExceeded` (Phase 1817)

```text
container '{nodeId}' holds {measured} direct children; this host allows {limit} (maxChildren, host limits '{limits}'). Group the children into sub-containers, or move the repeated items into a list or grid
```

#### `FUARAN161` — Error

`HostGridRowsExceeded` (Phase 1817)

```text
grid '{nodeId}' carries {measured} inline rows; this host allows {limit} (maxGridRows, host limits '{limits}'). Trim the rows to the ones the reader needs, or bind the grid to a source the host pages
```

#### `FUARAN162` — Error

`HostPayloadBytesExceeded` (Phase 1817)

```text
the tree rooted at '{nodeId}' encodes to {measured} bytes; this host allows {limit} (maxSerializedBytes, host limits '{limits}'). Emit less content, or move large inline data (static rows, long text) behind a bound source
```

#### `FUARAN163` — Warning

`ActionColumnField` (Phase 1909)

```text
grid '{nodeId}' column '{columnLabel}' is an action column (Button / ButtonGroup cell) and declares field '{field}' — an action cell draws its own label and hands the whole row to its handler, so the field is never displayed, and sort and export ignore it; drop the field: an action column carries none (Phase 1909)
```

<!-- END GENERATED: PreEmitValidate.DefectCodes.toMarkdown -->

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
