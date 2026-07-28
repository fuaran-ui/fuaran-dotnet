# 692–694 — the swap onto the generated types: staged execution plan

**Branch:** `phase-692-694-swap`. Main stays green and unswapped until the final stage lands whole.

This plan replaces the "all-or-nothing" framing. That property was real for `NodeKind` (one DU —
the flattening, already merged) but does NOT hold for the rest of the vocabulary: `Binding`,
`Action`, `TextSource`, the value unions and the records are each their own type, and each can be
switched to a **type abbreviation over `Fuaran.UI.Generated`** in its own green-gated stage. The
abbreviation mechanism is proven (spike, 2026-07-28): an `[<RequireQualifiedAccess>]` DU constructs
and matches through an abbreviation, generic instantiation and record construction included, and
`Generated.encodeNode` accepts abbreviated values.

**Stage 0 is DONE and on both mains** (fuaran-core `ac55082`, fuaran-dotnet `7648dbd`):
full `Binding` case parity (the gap-closure, D3), `TextSource.I18n`, and — load-bearing —
**generated case-field order now matches the hand-written positional order** (wire-free: the
canonical renderer sorts keys, the decoder reads by name; proven byte-identical). Construction and
match sites therefore compile unchanged wherever *arity and payload shape* match; only the deltas
below need touching.

## Per-stage plan

Each stage: alias the type(s) in `Types.fs` (delete the hand-written definition, keep companion
modules/members, fix their bodies), let the compiler enumerate the sites, apply the delta table,
then the full gate: Fantomas → FAKE `Test` (all suites + C#/VB conformance) → `dotnet fable`
samples/demo → the corpus byte-gates (`GeneratedLayerTests` 85/85 + the hand-written leg).

### Stage 1 — `Binding<'T>` + satellites — **DONE (branch commit `baba9a7`, all gates green)**

Landed 2026-07-28: 73 files, full FAKE Test (incl. 352 C# + 338 VB conformance, GeneratedLayerTests
85/85, JsonDecode 510/510), Fable demo clean, zero corpus byte changes. `SelectOption` / `RangePair`
proved to be stage-3 concerns (they ride `FormFieldKind`) and were not needed here. Two findings the
later stages must respect:

1. **Store values stay RAW.** The Filter / State / Selection stores hold raw host values (`box
   "eng"`, the raw clicked row) — resolving a `Binding<JVal>` source AT `JVal` unbox-throws on .NET
   and silently mis-matches under Fable erasure. `BindingResolver` therefore resolves Transform
   params / I18n args at `obj` through an explicit `objOfJValBinding` erasure and coerces afterwards
   (`:? JVal` → typed cell/arg projection; anything else raw). Any future JVal-typed store read must
   go through the same seam.
2. **Absence still routes through the slot's parser.** `{"$type":"Static"}` and the legacy
   `"value": null` §16 shorthand decode by handing `JNull` to the slot's own `parseStatic` (an
   options slot normalises to `[]`, an option-typed slot to inner `None`, a scalar slot rejects) —
   a decoder shortcut that mapped null straight to outer `None` broke the
   `lenient-null-static-options` byte gate. Keep the parser in the loop; the outer/inner absence
   forms reconcile at the encoder's `isAbsentPayload` check.

#### The original stage-1 delta table (as planned)

~98 files reference `Binding` cases. Residual deltas (order already matches):

| Site shape | Hand-written | Generated | Fix |
|---|---|---|---|
| `Static` payload | `'T` | `'T option` | `Some`-wrap; `None` = absent-on-wire |
| `Query.dependsOn` | `string list` | `string list option` | wrap; `None` ⇔ `[]` |
| `State.defaultValue` | `'T` | `'T option` | wrap (null-absence becomes typed absence) |
| `Selection.nodeId` | `NodeId` | `string` | unwrap (or see "wrapper types" below) |
| `Local` payload | `LocalBinding<'T>` record | 5 positional fields `(flushOn, format, initialFrom, onCommit option, parse)` | destructure; `LocalBinding` deleted |
| `I18n.args` | `Map<string, Binding<obj>> option` | `Map<string, Binding<JVal>> option` | box→`JVal` at the few sites |
| `Transform.parameters` | `(string * Binding<obj>) list` | `TransformParam list option` (`{From: Binding<JVal>; Name}`) | map tuples→records |
| `Invoke.args` | `(string * string) list` | `InvokeArg list` (`{Addr; Value}`) | map tuples→records |
| `Computed` fn | `BindingContext -> 'T` | `obj -> 'T` | unbox at sites (see open question) |

`CanonicalJson.encodeBindingWith` and `JsonDecode.bindingGeneric` re-target the generated shapes in
this stage (they keep existing — policy layer — but construct/consume the new type). The corpus is
the byte gate.

### Stage 2 — `Action<'Msg>` + `CallResultTarget` — **DONE (branch commit `2bb836f`, all gates green)**

Landed 2026-07-28: 27 files, full FAKE Test + Fable demo, zero corpus byte changes. As planned:
`Call` carries a bare endpoint string (`ApiEndpoint` survives at the author surface + the
`IFuaranRuntime` seam, re-wrapped at boundaries); `CallResultTarget` case names are the wire tags;
`Invoke` args are `InvokeArg` records. One addition over the plan: `ReadFileBody` did NOT erase
`FileRef` to a bare string — the record's `Handle` (the boxed browser `File` blob) is what the
runtime reads, so the generated case gained a **host-only `fileHandle: obj option` slot**
(Fuaran-Core `399ce96`, wire-invisible) beside the wire `fileRef` id, and the runtime seam keeps
its `FileRef` record, rebuilt at the render boundary. The wrapper-erasure open question (1) has its
answer pattern now: erase when the wrapper is pure naming (`ApiEndpoint`, `NodeId`), add a host-only
slot when it carries runtime state (`FileRef.Handle`).

### Stage 3 — `TextSource`, enums, `CellFormat`, `ColumnWidth`, records, `FormFieldKind` — **DONE (branch commit `f5d2a70`, all gates green)**

Landed 2026-07-28: 59 files, full FAKE Test (352 C# + 338 VB conformance, JsonDecode 510/510),
Fable demo AND catalog clean, zero corpus byte changes. As planned, plus:

- `FilterSpec.Field` → `.Kind` rode along (the generated record's field name); `SelectOption` /
  `MapMarker` labels are bare strings; `TabHeader.Icon` is `string option`.
- **Absence became a codec concern twice** (both caught by the 510-test byte gate, neither by the
  compiler): `HoleDecl`'s newly-typed `Scalar` default needs its own `encodeScalarTyped` (the boxed
  `obj` sniffing path collapsed it to `"<opaque>"`), and a Choice/SegmentedChoice
  `{"$type":"Static"}` with absent payload is a first-class `Static None` (no selection) — a
  dedicated `decodeBindingChoiceValue` bypasses the scalar-string parser that finding (2) above
  otherwise mandates. The stage-1 "keep the slot's parser in the loop" rule stands; the choice slot
  is the one place the generated `Static of 'T option` makes absence itself the value.
- **Renderers mirror the decode-time auto-bind at render time**: a `None` value slot substitutes
  exactly the binding `valueOr` synthesises (typed-default `State`, default-less `State` for choice,
  `Filter(name, None)` on chips) before the pre-existing arm body, and `keysOfFormFieldKind`
  contributes the auto-binding's keys — decoded-tree DOM/HTML and reactive subscriptions unchanged,
  hand-built `None` trees correct.
- **Fable-vs-.NET name-resolution hazard**: `Fuaran.Core`'s source distribution now ships its own
  `Deferred` (Pending/Ready/**Failed**); `open Fuaran.Core` after `open Fuaran.UI.Types` made the
  catalog Fable leg resolve `Deferred<obj>` to Core's while `Deferred.Error` stayed the UI's —
  incomplete-match + unification errors under Fable only. `BindingResolver` now fully qualifies
  `Fuaran.UI.Types.Deferred`. Any file opening both namespaces around a shared type name is exposed
  to the same class; the catalog Fable leg is the gate that sees it.
- `ColumnErased` / `CellKindErased` stayed hand-written this stage (they reference `Node` — stage 4).

### Stage 4-prep — HostPrelude + THosted typed host surface — **DONE (branch `f5b00ae`; Core `d0639d8`/`6d24e4c`)**

The IDL's documented `TStr` gap on `Accessibility.role`/`liveRegion` closed via `THosted`: the tier's
new `src/Fuaran.UI/HostPrelude.fs` (compiled ahead of `Generated.fs`) hosts `AriaRole` /
`LiveRegionKind` / `ErrorKind` / `ErrorPayload` / `FileRef` / `FileSelection` + wire codecs
(lower-case mapping, `Custom` passthrough); a byte-identical stub in Fuaran-Core's test assembly
(`UiHostPrelude.fs`) lets the generated snapshot compile there. `StateBehaviour.OnError` takes
`ErrorPayload` (not `obj`), `FileUpload.onSelect` takes `FileSelection list`, Sparkline's source
widened to `float list` (whole floats render integer-form — bytes unchanged). **The pattern:** a
THosted slot keeps the tier's typed surface wherever erasure would eat a real DU; pure-naming
wrappers still erase.

### Stage 4a — Node-free specs + `SemanticStyle` + `Accessibility` — **DONE (branch commit `af7afaa`, all gates green)**

Landed 2026-07-28: 35 files. The 17 display specs, Button/Select/FileUpload/Form/Map specs,
`SemanticStyle`, `Accessibility` alias `Generated.*`; `FileRef`/`FileSelection` alias the prelude.
Deltas: Icon fields → `string option`; Sparkline/Map sources → list-typed bindings;
`SelectSpec.Value` → `Binding<string>` (Static-None no-selection, the stage-3 choice collapse);
`SelectSpec.Multiple` → `bool option`; `FileUploadSpec.OnSelect` optional + FileSelection-typed;
Accessibility ids → bare strings, Role/LiveRegion typed via THosted. Full FAKE Test (352 C# + 338 VB
conformance), Fable demo + catalog, corpus untouched. **The constraint that shaped the split: a
generated spec carrying `Node<'Msg>` cannot alias before the envelope itself swaps** — which is what
stage 4b is.

### Stage 4b — layout/meta specs + `NodeKind` + `Node` (the 692 switch proper) — **IN PROGRESS**

Four IDL findings surfaced by the 4b sweep, all resolved IDL-side (never absorbed tier-side),
regenerated + re-synced before the tier switch continued:

1. **`StaticRows` cells are `TextSource`, not `string`.** The planned "fidelity narrowing
   (literals only)" was checked against the codec: the hand encoder renders each cell via
   `encodeTextSource` (a `Literal` IS the bare wire string — 0.2.0) and the decoder accepts
   `Bound` / `I18n` objects per cell, so `TStr` would have made stage 5's decoder reject or
   stringify documents that decode today. Table-1 bytes unchanged (all-literal cells).
2. **`CellValue` moved to the host prelude** (`Fuaran.UI.HostPrelude.CellValue`, stub mirrored
   in Core's `UiHostPrelude.fs`; `Types.CellValue` aliases it). The closure slots
   (`ColumnErased.Value`, `CellKindErased.Editable`, `CellFormat.Custom.fn`) keep the typed
   surface — the stage-3 obj-erasure of `CellFormat.Custom` is un-erased, and the planned
   box/unbox ceremony at `Column.erase` is gone. `FileSelection` precedent: closure-interior,
   never serialises, no codec.
3. **`Progress.labelFn` keeps its option** (the generated `req` dropped a real tier state — a
   progress cell with no label). Wire: sentinel-when-Some, omit-when-None; no fixture pins the
   None-label emission.
4. **`Tabs.orientation` exists** — the IDL comment "0.2.x dropped Tabs.orientation" was wrong;
   the hand encoder emits it omit-when-Horizontal and the decoder restores the default. No
   corpus fixture is Vertical, which is how the byte gate missed it (the stage-3b
   `BoxRole.Separator` class). Modelled `omit … (VEnum "Horizontal")`.

Encoder/decoder alignment for newly-optional closure slots (Editable.onEdit, Checkbox.onToggle,
Button.onClick, ButtonGroupItem.onClick, Progress.labelFn, Mount.onBubble, Stepper.onSelect):
the hand codec now emits sentinel-when-Some / omits-when-None and decodes presence→`Some` no-op /
absence→`None` — byte-identical for every previously-expressible value (the slots were required
pre-swap, so every existing document has the key), and aligned with the generated codec's form.
`TreeOp.UpdateStyle` / `UpdateState` normalise a default-valued payload to the `None` envelope
slot (a `Some Defaults.style` would emit `"style":{}` where omission is canonical).

#### Original 4b scope (for reference)

- Remaining after 4a: the Node-carrying layout/meta specs (Box, SplitPanel, Tabs, Stepper,
  SummaryList, Disclosure, Modal, ScrollArea, ErrorBoundary, Switch, Mount, FragmentDecl,
  FragmentRef), `StateBehaviour`, `ChartSpec` (its `Binding<unit>` source needs an IDL THosted
  round first — `Binding<obj seq>` is the tier truth), `CustomSpec` / `FiltersSpec` /
  `DataGridSpec` (NodeKind case-payload changes), then `NodeKind` + `Node` + the `NodeId` erasure
  (1345 refs / 192 files at last count).
- `Fuaran.fs` / `Defaults.fs` construct the generated specs (692 tasks 1–2); reconcile `mk<Kind>`
  constructors vs hand-written smart constructors (692 task 3 — delete the loser).
- Node envelope: hand-written `State` / `Style` non-option records (omit-when-empty) become
  `option` (absence is structural — same D1/D3 argument).
- `BoxLayout`/`FlexLayout`/`GridTemplate` → generated `LayoutMode` (`Auto | Flex(direction, wrap) |
  Grid(cols, templateColumns option)`) — nested records become positional case fields.
- `StaticRows`: hand `(TextSource list * TextSource list list) option` → generated record
  `{Headers: string list; Rows: string list list}` — a fidelity narrowing (literals only); if a
  non-literal staticRows use exists, that is a finding to take back to the IDL, not to absorb.
- Wrapper erasures: `FragmentId`, `CapabilityTag` → `string` in generated specs.
- Retired spec types (`DashboardSpec`, `StackSpec`, `CardSpec`, `GridLayoutSpec`) — verify dead
  (0.2.0 Box unification) and delete.

### Stage 5 (693) — renderer + apply engine internals; Stage 6 (694) — deletion + measurement

Stages 1–4 already force most of the renderer/apply compile fixes. 693's residue: `JsonDecode`
decodes INTO the generated types everywhere (keeping diagnostics/§16 policy), the resolver reads
the new shapes. 694: delete the hand-written structural definitions that remain, delete
`CanonicalJson.encodeNode` in favour of `Generated.encodeNode` (the TreeOp codec re-points at it),
then run the add-a-kind mirror-count measurement the phase mandates.

## Open questions (decide at the stage, not silently)

1. **Wrapper types** (`NodeId`, `ApiEndpoint`, `BindingContext`, `FileRef`): the generated slots
   erase them to `string`/`obj` because the sig strings must compile Core-side too (Core has no
   `Fuaran.UI` types). Options: accept erasure (current plan; record in STABILITY as part of the
   major bump) or teach the emission to be host-parameterised. Do NOT hand-patch Generated.fs.
2. **`Deferred<'T>`**, `BindingContext`, `II18nResolver`, `Theme`, validator/prop-schema types stay
   hand-written — host/runtime surface, not wire vocabulary (D3).
3. One `STABILITY.md` entry covers the whole swap when it lands on main — one major bump, not six.
