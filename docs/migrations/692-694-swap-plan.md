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

### Stage 1 — `Binding<'T>` + satellites (`Format`, `LocaleSource`, `LocalFlushTrigger`, `SelectOption`, `InvokeArg`, `TransformParam`, `RangePair`)

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

### Stage 2 — `Action<'Msg>` + `CallResultTarget`

Deltas: `Call`'s `ApiEndpoint` unwraps to `string`; `ReadFileBody.file: FileRef` → `fileRef: string`
and `onRead` becomes `option`; `CallResultTarget` case NAMES change (`IntoState`/`IntoQuery` →
`State`/`Query` — the wire tags; few sites).

### Stage 3 — `TextSource`, enums, `CellFormat`, `ColumnWidth`, `CellKindErased`, records, `FormFieldKind`

- `TextSource.I18n` args match exactly (`Map<string, JVal>`) — alias is clean.
- `FormFieldKind`: every `value` slot `option`-wraps (absence = auto-bind, newly authorable);
  `NumberFieldConstraints` / `DateFieldConstraints` records flatten to positional `min`/`max`/`step`
  options; `Range`'s `(float * float)` pair becomes the `RangePair` record; `FilterKind` is already
  gone (FilterSpec holds FormFieldKind).
- `ColumnErased.Value` is `option` (sibling of `Field`), `CellFormat.Custom` fn arg erases to `obj`.

### Stage 4 — specs + `NodeKind` + `Node` (the 692 switch proper)

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
