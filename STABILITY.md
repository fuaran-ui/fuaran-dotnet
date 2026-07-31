# Fuaran language-tier stability policy

This document declares which Fuaran *language-tier* surfaces are stable, what counts as a breaking change in each, and the semver rules that govern the `Fuaran.UI.*` NuGet packages shipped from this repo. It is the contract that downstream consumers (runtime tiers, demo applications, third-party adopters) can rely on when pinning a Fuaran version.

> This document scopes to the **language tier** – the `Fuaran.UI.*` packages shipped from this repo.

## Scope

| Package | Licence | Version status |
|---|---|---|
| `Fuaran.UI` | Apache-2.0 | pre-1.0 |
| `Fuaran.UI.Renderer.Core` | Apache-2.0 | pre-1.0 |
| `Fuaran.UI.Renderer` | Apache-2.0 | pre-1.0 |
| `Fuaran.UI.Ops.Abstractions` | Apache-2.0 | pre-1.0 |
| `Fuaran.UI.Ops` | Apache-2.0 | pre-1.0 |
| `Fuaran.UI.AiTools` | Apache-2.0 | pre-1.0 |
| `Fuaran.UI.Validator` | Apache-2.0 | pre-1.0 |
| `Fuaran.UI.OpStream.*` | Apache-2.0 | pre-1.0 |
| `Fuaran.UI.LayoutObserver.*` | Apache-2.0 | pre-1.0 |
| `Fuaran.UI.StyleObserver.*` | Apache-2.0 | pre-1.0 |
| `Fuaran.UI.ThemeManifest` | Apache-2.0 | pre-1.0 |
| `Fuaran.UI.Telemetry.*` | Apache-2.0 | pre-1.0 |
| `Fuaran.UI.Memo` | Apache-2.0 | pre-1.0 |

The language tier is licensed **Apache-2.0**. See [`LICENSE`](LICENSE).

## Pre-1.0 caveat

**Until `Fuaran.UI` ships `1.0.0`, every minor version may break.** Renderer dispatch, tree-op apply semantics, AI tool surface, op-stream wire shape, layout-observer flags, and telemetry sink contracts are all still stabilising. Consumers pinning a pre-1.0 version should pin to the exact patch (`X.Y.Z`, not `X.Y.*`) and plan on per-bump audit.

The semver rules below take effect from `1.0.0` onward. Pre-1.0, they are aspirational – they describe what kind of change is *intended* to be a major-vs-minor bump, but Fuaran does not yet promise to honour them at the package-version layer.

## Semver

For each package, from `1.0.0` onward:

- **Major** (`X.0.0`) – any change to a stable surface that requires consumer source-code edits to compile or behave equivalently.
- **Minor** (`x.Y.0`) – backward-compatible feature addition. Existing consumer code keeps compiling. New stable surfaces may appear; existing ones must not change in incompatible ways.
- **Patch** (`x.x.Z`) – backward-compatible bug fix. No surface change.

DU exhaustiveness warnings (F#'s `FS0025`) are *not* considered breaking. Adding a `NodeKind` DU case is minor, not major – existing consumers compile with a warning that they don't handle the new case, which is the correct signal.

> **Whether a kind change is admitted at all is governed separately.** This document classifies the
> *version bump* of a `NodeKind` change (addition = minor pre-1.0; removal / `$type` rename = major – 
> a breaking wire-format change, see [Wire format](#wire-format) below). **Whether a new kind should be
> added in the first place** – the demand-evidence, irreducibility, cost, and confusion gates – is the
> [vocabulary-growth governance charter](docs/VOCABULARY.md). The two agree by construction: a kind
> retirement (e.g. a near-synonym merge) is a major wire event here *and* the charter's reason to
> schedule such merges **before** the `1.0.0` publication. Every plan that changes the kind set clears
> the charter's admission gates and carries the `**Stability impact:**` annotation this document scans
> for.

## Stable surfaces

The following are covered by the semver rules above:

### `Fuaran.UI`
- **Phase 692 (BREAKING — landed in 0.8.0): `NodeKind<'Msg>` is flat.** The
  four behavioural-category wrapper cases (`Layout of LayoutKind` / `Display of DisplayKind` /
  `Input of InputKind` / `Visualisation of VisKind`) are removed, and the 33 kinds they wrapped are
  now direct cases of `NodeKind<'Msg>` beside the six structural ones; the `LayoutKind` /
  `DisplayKind` / `InputKind` / `VisKind` DUs no longer exist. This matches the shape the wire has
  always declared canonical (`WIRE_FORMAT.md` §3.2 — the categories were "a host-side classification
  recovered on decode") and is the precondition for the IDL-generated structural layer becoming the
  authoring type. The classification itself survives as the derived `NodeCategory` DU +
  `Kind.category`. **Migration:** `NodeKind.Display(DisplayKind.Heading s)` becomes
  `NodeKind.Heading s` (construction and match sites alike); code that dispatched on the category
  wrappers matches on `Kind.category kind` instead. Smart-constructor authoring (`Fuaran.button` …)
  is unchanged.
- The §4b record contract (per [`src/Fuaran.UI/Types.fs`](src/Fuaran.UI/Types.fs)) – `Node`, `NodeKind`, `NodeId`, `Binding<'T>`, `Action<'Msg>`, `StateBehaviour`, `Column`, `CellFormat`, `SemanticStyle`, `Display`, and every other record / DU exported from `Types.fs`. Theme-as-API adds `Theme`, `ColorVar`, `ToneStops`, `Tones`, `Spacing`, `FontScale`, `FontWeight`, `LineHeight`, `Radius`, `ButtonSize`; the interaction-state extension adds `Interaction`, `ToneStateMatrix`, `FocusRing`.
- Phase 62 additions to `Binding<'T>` / `Action<'Msg>` – `Binding.Local`, `LocalBinding<'T>`, `LocalFlushTrigger` (cases `OnBlur` / `OnSubmit` / `OnDebounce of int` / `OnCommitAction`), `Action.CommitLocal of string`. Stable on the same Pre-1.0 minor-bump axis as the rest of the §4b contract.
- Phase 64 addition to `Action<'Msg>` – `Action.WriteToClipboard of text: string` (the typed clipboard-write intent). Renderer-side substrate: `IFuaranRuntime.WriteToClipboard: text: string -> unit`. Stable on the same Pre-1.0 minor-bump axis as the rest of the §4b contract.
- Phase 136 addition to `Action<'Msg>` – `Action.ReadFileBody of file: FileRef * encoding: FileReadEncoding * onRead: (string -> 'Msg)` (the typed file-read intent). New top-level types: `FileRef` (record `{ Id: string; Handle: obj option }`) and `FileReadEncoding` DU (`Text` / `Base64` / `DataUrl`); `FileSelection` gains a required `Ref: FileRef` field (the renderer constructs `FileSelection`, so no consumer-side record literal breaks – `OnSelect` receives the value). Renderer-side substrate: `IFuaranRuntime.ReadFileBody: file: FileRef * encoding: FileReadEncoding * onRead: (string -> unit) -> unit`. Smart-ctor: `Action.readFileBody`. **Additive case on `Action` – existing pattern matches gain an `FS0025` warning, the correct minor-bump signal.** Stable on the same Pre-1.0 minor-bump axis as the rest of the §4b contract.
- Phase 70 additions to `NodeKind.Custom` – the existing `Custom of moduleId * componentId * props` case extends to `Custom of moduleId * componentId * props * contentHash: ContentHash option * exposedNodeIds: NodeId list`. New top-level types: `ContentHash` (record with `Algorithm` / `Hash` / `Strictness`) and `HashStrictness` DU (cases `StrictReplay` / `AdvisoryWarning`). Stable on the same Pre-1.0 minor-bump axis as the rest of the §4b contract. Pre-Phase-70 consumers pass `None` + `[]` for the two new fields; the `Fuaran.custom` smart-ctor is the recommended construction surface going forward.
- Phase 102 additions to `Binding<'T>` – `Binding.Format of source: Binding<float> * format: Format * locale: LocaleSource` (the locale-aware formatted-value case, constrained to `Binding<string>` use). New top-level types: `Format` DU (cases `Number of int option` / `Currency of string` / `Percent of int option` / `Date of DateStyle` / `RelativeTime of RelativeTimeUnit`), `DateStyle` DU (`Short` / `Medium` / `Long` / `Full`), `RelativeTimeUnit` DU (`Second` / `Minute` / `Hour` / `Day` / `Week` / `Month` / `Year`), `LocaleSource` DU (`Ambient` / `Explicit of string`). **Additive case on `Binding` – no breaking change to existing constructors** (existing pattern matches gain an `FS0025` warning, which is the correct minor-bump signal). Renderer-side substrate: `BindingResolver.BindingSources.Locale: string` (the ambient-locale source, identity-default `""`) + the `Fuaran.UI.Renderer.Formatting` module. Stable on the same Pre-1.0 minor-bump axis as the rest of the §4b contract.
- Phase 53 – the `Fuaran.UI.Bounded` "parse, don't validate" bounded-scalar kit (single-case DUs with private validating constructors): `NonEmptyString`, `BoundedString` (`[minLen, maxLen]`), `BoundedInt` (`[min, max]`), and `Fraction` (`[0, 1]`), each with a `tryCreate : ... -> Result<T, string>` factory and a total `value` projector. **Additive and non-breaking – no wire-format impact.** The kit does not alter any §4b spec field; existing specs keep their primitive leaves, and the wire encoder/decoder are unchanged (no new `NodeKind` / `Spec` / `TreeOp` / `Binding` / `Action` case). A core contract that wants the by-construction in-range guarantee opts in field-by-field; until it does, the kit is a free-standing utility on the same Pre-1.0 minor-bump axis as the rest of `Fuaran.UI`. The companion build-time validator rule `ScalarRangeCheck` (FUARAN050) is **advisory only** – it emits a `Warning` (never an `Error`) when a statically-knowable `Fuaran.progress` `Fraction` literal falls outside the known-bounded `[0, 1]` domain, so it does not fail the build and is safe to adopt incrementally (consistent with the other advisory rules FUARAN045/046/053–058).
- Phase 180 – parameterised fragments (the artifact-function lift). `FragmentDeclSpec<'Msg>` gains `Holes: HoleDecl list` + `Effect: EffectClass`; `FragmentRefSpec` becomes generic `FragmentRefSpec<'Msg>` with `Args: Map<string, FragmentArg<'Msg>>`. New top-level types: `HoleValueSpace` (`IntRange` / `FloatRange` / `StringLen` / `Enum` / `AnyString`), `HoleDecl` (`Value` / `Slot` / `Repeat`), `HostEffect`, `DeterminismSource`, `EffectClass`, `FragmentArg<'Msg>` (`Value` / `Slot`); `ParamFragment<'Msg>` is now an alias for `FragmentDeclSpec<'Msg>`. **Additive on the wire – a zero-hole, pure-deterministic decl omits `holes`+`effect` and a zero-arg ref omits `args`, so a fixed-body fragment round-trips byte-identically (the degenerate case).** Adding record fields is technically a recompile for any consumer that constructs `FragmentDeclSpec`/`FragmentRefSpec` via a record literal (the `Fuaran.fragmentDecl` / `Fuaran.fragmentRef` smart-ctors shield this – they fill the new fields); making `FragmentRefSpec` generic is a recompile for any direct type annotation. Per the precedent below, a **pre-1.0 minor add**. New validator codes FUARAN059 (totality – non-bounded `Repeat` count) + FUARAN065 (a value hole's literal default outside its value-space) are **errors**, lifting the runtime `HoleDecl.isTotal` / `HoleValueSpace.validate` predicates to the build-time AST walker. Stable on the same Pre-1.0 minor-bump axis as the rest of the §4b contract.
- Phase 323 – the runtime query-result-schema → `Binding<'T>` typed-thread check (`Fuaran.UI.QueryBinding`). New public surface: the `BindingSinkClass` DU (`Numeric` / `Temporal` / `Categorical` / `Boolean`), `compatible : ColumnType -> BindingSinkClass -> bool` (the pure default-deny relation), `compatibleColumnTypes`, `QueryBoundRef`, `Defect`, `queryBoundRefs`, and `check : Schema -> Node<'Msg> -> Defect list` (over `Fuaran.Core.Schema`). Runtime codes FUARAN066 (incompatible sink) + FUARAN067 (absent column) – **errors**, distinct from the AST-validator codes (this is a runtime relation, not an `Fuaran.UI.Validator` rule; F#'s own type system covers the static case). **Additive, FSharp.Core + `Fuaran.Core.Column`-only, Fable-clean – no wire-format impact, no change to any existing surface.** Stable on the same Pre-1.0 minor-bump axis as the rest of the §4b contract.
- The `Defaults.X` field set per X (per [`src/Fuaran.UI/Defaults.fs`](src/Fuaran.UI/Defaults.fs)).
- The public smart constructors `Fuaran.X "id" { ... }` (per [`src/Fuaran.UI/Fuaran.fs`](src/Fuaran.UI/Fuaran.fs)). Phase 62 adds `binding.local`. Phase 70 adds `Fuaran.custom`.

### `Fuaran.UI.Renderer.Core`
- **Phase 138 – the emission-agnostic render spine.** `Fuaran.UI.Renderer.Core` is the new `FSharp.Core`-only (plus `Fable.Core` for the `Formatting` Intl branch) package holding the parity-critical logic the client (Feliz) and server (Feliz.ViewEngine, Phase 140) renderers must compute identically: the `Fuaran.UI.Renderer.Sanitize`, `.Theme`, `.Formatting`, `.BindingResolver`, `.Accessibility`, and `.Ids` modules.
- **Namespace-preservation guarantee.** The four modules extracted out of `Fuaran.UI.Renderer` (`Sanitize`, `Theme`, `Formatting`, `BindingResolver`) **keep their original `Fuaran.UI.Renderer.*` module names** – they live in a different assembly now, but any consumer that imported `Fuaran.UI.Renderer.Sanitize` / `.Theme` / `.Formatting` / `.BindingResolver` sees **no source change**. `Fuaran.UI.Renderer` takes a package reference on `.Core`. This is a **non-breaking refactor by construction** (not even a minor-surface change for consumers); it appears here so the new package's stable surface is declared.
- `Fuaran.UI.Renderer.Ids` – `deterministicCorrelationId : string -> string` (FNV-1a → 8 hex chars) + `randomCorrelationId : unit -> string` (escape hatch). The renderer's error-correlation ids are now derived deterministically from the failing node's id so identical trees render byte-identical output (cache-stable + SSR/hydration-parity-safe). Correlation-id *values* are not a stable contract (they are diagnostic), but the determinism *property* is.
- `Fuaran.UI.Renderer.Accessibility.accessibilityAttributes : BindingSources -> Accessibility option -> (string * string) list` – the renderer-neutral aria/role attribute projection. Re-exported as `Render.accessibilityAttributes` (unchanged for existing callers).

### `Fuaran.UI.Renderer`
- The `Render` entry point and its signature.
- The `Theme` and `BindingResolver` public surfaces consumed by Renderer extensions. _(As of Phase 138 these modules physically live in `Fuaran.UI.Renderer.Core` with their `Fuaran.UI.Renderer.*` namespaces preserved; the contract is unchanged.)_
- **Decision (Phase 119): the renderer owns a dispatch policy-gate seam.** `IFuaranRuntime.CanDispatch : ActionDescriptor -> bool` is consulted by `runAction` before the gated host effects (`Call` / `Navigate` / `AiTool` – Phase 136 adds `ReadFileBody` to the gated set via the additive `ActionDescriptor.ReadFileBody of fileId: string` case); on deny the renderer emits a `Warn` diagnostic and skips the effect. The language tier therefore exposes its **own** default-deny seam rather than deferring the gate strictly to a downstream orchestration tier – a standalone host (e.g. the BYOK browser playground) consuming only the public packages can make the dispatch path default-deny without a §4j orchestration gate in the loop. The default runtimes (`Diagnostic` / `Mutable` / `Browser`) return `true` (allow), so a host that does not gate behaves exactly as before. Per the established precedent below, adding the `CanDispatch` abstract member is a **pre-1.0 minor add** – direct `IFuaranRuntime` implementers add `member _.CanDispatch _ = true` to preserve allow-by-default. (F# interfaces cannot carry a true default implementation, so the new member is technically a recompile for direct implementers; all in-repo implementers were updated in the same change, and there are no cross-sibling direct implementers.)
- **The render-entry family and the axes each one pins.** `render` (the general `RenderContext` entry) plus the convenience entries `renderWithSources` / `renderWithSourcesAndSink` / `renderWithSourcesSinkAndContext` / `renderWithSourcesInScope` / `renderWithSourcesInScopeAndSink`, and the composing wrappers `renderWithTheme` / `renderStateReactive`. **Which `RenderContext` axis an entry PINS is part of the contract, not an implementation detail** – a host picks its entry by exactly that. Adding an entry is additive; changing an existing entry's pinned axis (giving `renderWithSources` a real runtime, say, or pinning a sink where a host supplies one) is a behavioural change to every consumer of that entry and bumps accordingly. The full grid is [`docs/RENDER-ENTRIES.md`](docs/RENDER-ENTRIES.md).
- **`GuestSeam` – the host-pluggable `Mount` guest capability seam.** `GuestSeamContext` (`ScopeId` / `Capabilities` / `Channel`), the `GuestSeam` record (`WrapRuntime` / `GateBubble`), and `installGuestSeam` / `clearGuestSeam` / `currentGuestSeam`. The **default-off** property is the load-bearing part of the contract: with no seam installed the `Mount` arm hands the guest the host runtime and an unwrapped bubble, exactly as before the seam existed. Making the seam mandatory, or changing the no-seam default, is a breaking change for every host that installs nothing.
- The `Sanitize` module's policy contract – `sanitizeExtraAttributes`, `sanitizeUrl` / `sanitizeUrlOrBlank`, `sanitizeMarkdownHtml` (Phase 56). Tightening the policy (rejecting an attribute key or URL scheme previously accepted) is a behavioural change to renderer output and counts as a minor-version bump; loosening it (accepting an attribute key or URL scheme previously rejected) is additive. The injection-safety contract is documented separately in [`SANITIZATION.md`](SANITIZATION.md), which the renderer leans on as the source of truth for which inputs are neutralised at render time.

### `Fuaran.UI.Ops`
- The `TreeOp<'Msg>` DU (the §4g op vocabulary).
- The `ApplyError` / `ApplyErrorCode` / `ApplyHint` records (the §4d AI-recovery error shape).
- The `Apply.apply : TreeOp<'Msg> -> Node<'Msg> -> Result<Node<'Msg>, ApplyError>` entry point.
- **Id uniqueness (§4g) now actually holds across the whole tree** (0.3.3). Traversal was built on
  `getChildren`, which answers only "which kinds accept the structural ops", so nodes held in
  non-list positions — `Switch.Cases`/`Default`, `ErrorBoundary.Child`/`Fallback`, the
  `StateBehaviour.OnLoading`/`OnEmpty` slots on *every* node, and `FragmentArg.Slot` args — were
  invisible to `allNodeIds` / `findNode` / `collectNodeIdsInto`, and therefore to the duplicate-id
  rejection (which reaches Core through a witness whose `Children` IS `getChildren`). An
  `InsertChild` colliding with one of those ids was accepted. It is now refused. **This is a
  behavioural tightening**: a tree that was accepted before may be refused now, and that refusal is
  the guarantee working rather than a regression. `getChildren` is deliberately unchanged — a
  `Switch`'s cases are keyed, so `InsertChild(switchId, 2, node)` must not acquire a meaning — and the
  new `Introspect.descendantNodes` answers the traversal question separately.
- **`Introspect.availableFields` is the UpdateProp surface, and every entry must be reachable** (0.3.2).
  A name appears there only if `UpdateProp` sets it, or it is a `Binding` slot also listed in
  `availableBindingSlots`, or it is reached structurally (the literal `Children`, or a prefix of an
  `availableNestedPaths` entry). `Action` fields were listed and are not any more: a closure is not
  expressible as a wire value, so no op sets one. **Adding an unreachable entry is a defect, not a
  documentation improvement** — the hint is what an author reads after a refused edit, so a name it
  cannot satisfy sends them into a retry loop against their own correct emission. Enforced by
  `HintHonestyTests` over the wire-format corpus, which covers 39 kinds and fails with the offending
  `kind.field` named.
- **`UpdateProp.path` accepted spellings** (0.3.1) — the path resolver accepts the **camelCase** wire
  spelling of a field (`"subtext"`) as well as the PascalCase record-field spelling (`"Subtext"`), and
  tolerates a redundant leading **`kind.`** segment (`"kind.subtext"`), which is the path the field
  visibly occupies in the serialised tree. All three are part of the stable surface: **narrowing any of
  them back is a breaking change**, not a tidy-up. They exist because the wire is camelCase everywhere
  and nests spec fields under `kind`, so an author addressing a field by what the document in front of
  them shows was being refused for being consistent with it — measured, with two independent model
  families each picking one of the refused spellings. A bare `"kind"` with nothing after it is still
  refused (that addresses the spec wholesale, which is `EditNode`'s job), and a non-leading `Kind`
  segment (`"Columns[i].Kind"`, `"Fields[i].Kind"`) remains deliberately unaddressable.
- The `ErrorRender.render` entry point.
- `Fuaran.UI.Ops.JsonDecode.{decodeNode, decodeOp, DecodeError, DecodeErrorCode}` – the structural decoder mirroring `Fuaran.UI.OpStream.Abstractions.CanonicalJson.encodeNode` / `encodeOp`. **Forward-coupling rule:** adding a new `NodeKind` / `Spec` / `TreeOp` / `Binding<'T>` / `Action<'Msg>` case in any future phase MUST update the decoder in the same commit. **Wire-shape lock declaration** (Phase 62): the `Binding.Local` wire shape's `$type: "Local"` discriminator + the `LocalFlushTrigger` `$type` enumeration (`OnBlur` / `OnSubmit` / `OnDebounce` / `OnCommitAction`) + the `Action.CommitLocal` `$type` is part of the stable structural-decoder surface – JsonDecode breakage on any of these is a major-version bump. **Wire-shape lock declaration** (Phase 64): the `Action.WriteToClipboard` `$type` discriminator + its `text` field is part of the stable structural-decoder surface on the same axis. **Wire-shape lock declaration** (Phase 70): the `Custom` discriminator's optional `contentHash` (with `algorithm` / `hash` / `strictness` sub-fields) + `exposedNodeIds` array fields are part of the stable structural-decoder surface – JsonDecode breakage on either is a major-version bump. **Wire-shape lock declaration** (Phase 102): the `Binding.Format` `$type: "Format"` discriminator + its `format` / `locale` / `source` fields, the `Format` `$type` enumeration (`Number` / `Currency` / `Percent` / `Date` / `RelativeTime`) + its field names (`decimals` / `isoCode` / `dateStyle` / `unit`), the `LocaleSource` `$type` enumeration (`Ambient` / `Explicit` + `tag`), and the `DateStyle` / `RelativeTimeUnit` bare-enum string sets are part of the stable structural-decoder surface on the same axis. **Wire-shape lock declaration** (Phase 136): the `Action.ReadFileBody` `$type` discriminator + its `fileRef` (string) + `encoding` fields, and the `FileReadEncoding` bare-enum string set (`Text` / `Base64` / `DataUrl`), are part of the stable structural-decoder surface on the same axis (the blob handle + `onRead` continuation never serialise).

### `Fuaran.UI.OpStream.Abstractions`
- The op-stream type contract (`PersistedOp`, `OpRecord`, etc.).
- `CanonicalJson.encodeNode` / `encodeOp` – bumps jointly with the `Fuaran.UI.Ops.JsonDecode` decoder.
- Hash-chain primitive.
- **Chain format version (`StreamEntry.chainFormatVersion`, currently `2`).** The chain
  pre-image envelope leads with `{"v":<n>,…}`, folded first into the SHA-256 hash, so the
  format is **self-describing and tamper-evident**: `StreamEntry.formatVersion` lifts `v` from
  a record without verifying, letting a host reject an unrecognised format with a clear error
  rather than a cryptic chain break (a tagless record = the pre-406 **v1** format). **Bump `v`
  in lock-step across F#/TS/Python and the `chain-corpus.json` golden** whenever the pre-image
  formula, the envelope shape, or the `HashFn` changes – the shared corpus is the parity gate,
  and a host that computes a different pre-image fails it. The **DAG** content-address
  (`Fuaran.UI.OpStream.Dag`) is a separate F15-sovereign format and carries its own identity;
  a DAG version tag is tracked separately.
- **Elicitation envelope (Phase 465, additive).** `AnswerField` / `AnswerContract` /
  `AnswerValue` / `ElicitationEnvelope` / `ElicitationOutcome` / `ElicitationOutcomeEnvelope` +
  the `Elicitation` codec module (`encodeEnvelope` / `decodeEnvelope` / `encodeOutcome` /
  `decodeOutcome` / `validateAnswer` / `validateAnswerAt` / `validateAnswerDocument` /
  `encodeContractJson` / `decodeAnswerJson`) and `ElicitationErrorCode` – a **new, additive
  top-level wire artefact** (WIRE_FORMAT.md §18): the Node wire form is embedded unchanged and
  no `NodeKind` / `Action` / `Binding` case is added. Value spaces reuse
  `Fuaran.Core.ValueSpace` (the package gains a `Fuaran.Core.Function` reference). The wire
  shape (envelope keys, the `$elicitation` version tag, the outcome `$type` set, the §18.5
  error codes, the answer-conformance rules) is corpus-locked by the
  `wire-format-fixtures/elicitation/` families; breakage is a major-version bump on the same
  axis as the linear wire format. Existing fixtures are byte-unchanged.

### `Fuaran.UI.OpStream.Dag.*` (opt-in, rung-4 – Phase 178)

The branching-DAG generalisation of the op-stream. **Opt-in by design**: a
consumer that references only `Fuaran.UI` + `.Renderer` + `.Ops` + the *linear*
`.OpStream.*` packages pulls **none** of the DAG binary or API. The DAG packages
reference the linear abstractions; nothing in the linear ("light") path
references the DAG packages back. The merge engine *requires* `IDagOpStreamSink`,
so the entire branch/merge surface is structurally unreachable from the linear
path – a simple linear tree app carries zero DAG overhead.

- **Rung-4 packaging contract.** Asserted mechanically by a reference-graph
  check (`Fuaran.UI.OpStream.Dag.Tests` – every linear assembly's
  `GetReferencedAssemblies()` contains no `Fuaran.UI.OpStream.Dag.*` name, and
  the DAG abstractions *do* reference the linear abstractions). A future edit
  that makes any light-path package reference a DAG package turns the contract
  red. This is the executable form of the opt-in posture.
- **`Fuaran.UI.OpStream.Dag.Abstractions`** (stable surface): `DagOpRecord<'Msg>`
  (multi-parent, content-addressed; merge nodes commit to an outcome-tree hash),
  the lexicographically-sorted-parent hash algorithm, `DagTopology`
  (`reachable` / `lca`, the `LcaResult` DU), `DagVerify`, `DagRetention` +
  `ResumeCoordinate` + `DagCheckpoint`, the `RetentionPolicy` /
  `DagRetentionPolicy` / `RejectionTombstone` decision-content-split policy tier
  (Phase 179 – grace / auto-pin / purge-trigger over the mechanism; clocks only
  for grace, never in the hash path), and the `IDagOpStreamSink<'Msg>`
  superset interface (content-addressed `Add`, atomic `TryAdvanceHead` CAS,
  topology queries, tombstone pruning). `FSharp.Core` + the linear abstractions
  only – no orchestration-private dependency; Apache-2.0-clean.
- **`Fuaran.UI.OpStream.Dag.Merge`** (stable surface): the M1 merge contract – 
  fast-forward + disjoint-`(NodeId, facet)` auto-merge with a NodeId-canonical-
  bytes tie-break (no wall-clock); overlapping changes refuse with the contended
  cells; the merge-node hash commits to the canonical encoding of the resulting
  tree so two conformant hosts agree iff they reach the same tree. The M2 human-
  primacy additions (Phase 179) are additive: `TreeMerge.merge3WayWithCellAuthor`
  (per-cell authorship) + the `DagPrimacy` backward-walk that derives it +
  `ReassertPin` (KindSwapOrphansPin migration); the author-agnostic `merge3Way`
  and per-side `merge3WayWithAuthor` entry points are unchanged.
- **DagOpRecord wire shape** (Phase 178): the multi-parent record wire form is an
  **additive** wire artifact – the *linear* `OpRecord` + corpus are untouched.
  Its conformance fixtures live in `wire-format-fixtures/dag/`. Breakage of the
  `DagOpRecord` wire shape (the `parents` array, `outcomeHash`, the sorted-parent
  hash pre-image) is a major-version bump on the same axis as the linear wire
  format.
- **`ReplaceRoot` tree-op** (Phase 262): an **additive** `TreeOp` case – the
  whole-tree swap, `{ "$type": "ReplaceRoot", "node": <Node> }`, the only op that
  legally changes the root node id. Encoder + decoder + apply + schema + the
  `wire-format-fixtures/ops/op-replaceroot.json` corpus + the TS host
  (`@fuaran-ui/ops`) all ship together (the §11 forward-coupling rule). Existing
  ops + their fixtures are byte-unchanged; a decoder that predates `ReplaceRoot`
  rejects it as an unknown `$type` (forward-incompatible, as for any new case).

### `Fuaran.UI.LayoutObserver.Abstractions`
- The `LayoutFlag` DU + observer interface.
- The shared flag-derivation logic.

### `Fuaran.UI.StyleObserver.Abstractions`
- The `StyleFlag` DU (`ContrastBelowAA` / `InvisibleText` / `AccentIndistinct`, each carrying the observed `ratio`) + the `IStyleObserver` observer interface + the `StyleObservation` / `Rgba` / `FontRole` / `StyleObserverOptions` type contract.
- The shared flag-derivation logic (`Flags.effectiveBackground` composite walk, WCAG `contrastRatio`, `Flags.derive` / `toObservation`).
- **`StyleFlag` is additive-only post-ship.** Phase 144 shipped the three manifest-free cases (`ContrastBelowAA` / `InvisibleText` / `AccentIndistinct`); Phase 146 added four manifest-aware cases (`TokenResolutionFailed` / `OffPaletteColour` / `UsageBudgetExceeded` / `ContrastBelowDeclaredFloor`) – an additive minor bump (existing matches gain an `FS0025` warning). Redefining an existing case is a major bump (it breaks every AI prompt cache that pattern-matched it). The manifest-aware flags + the area-weighted budget verification (`ManifestFlags`) live in the concrete `Fuaran.UI.StyleObserver` package, which takes a `Fuaran.UI.ThemeManifest` dependency; the Abstractions package stays `FSharp.Core`-only (the four cases are plain data).
- The JSON encode shapes (`StyleFlag.encode`, `StyleObservation.encode`, `Rgba.encode`) – tagged-object camelCase, invariant-culture floats – mirror `LayoutFlag` / `LayoutObservation` exactly; the wire shape bumps on the same axis as the layout surface.

### `Fuaran.UI.ThemeManifest`
- The typed model: `ThemeManifest`, `ManifestMeta`, `ManifestToken`, `ManifestRole`, `RoleBinding`, `Invariant`, `InvariantKind`, `MotionBudget`, and the `JsonValue` portable-JSON AST.
- `ThemeManifest.resolveRole : ToneVariant -> ThemeManifest -> ManifestToken option` (+ `resolveNamedRole`, `tryGetToken`, `paletteColours`) – the lookup surface the `StyleObserver` consumes.
- `Encode.encode` / `Decode.decode` – the canonical-JSON contract. **DTCG-compatible:** a vanilla DTCG token file decodes to a manifest with empty `Roles`/`Invariants`; `$type`/`$value`/`$description` round-trip. The shipped `theme-manifest.schema.json` is the machine-readable contract.
- **`InvariantKind` is additive-only post-ship** (parallel to `StyleFlag`/`LayoutFlag`): adding a case is a minor bump (existing matches gain `FS0025`), redefining one breaks every authored manifest + AI prompt cache. Each `Invariant` carries a soft `Weight` (default `1.0`); weight-learning is a later phase.
- The manifest is a **host/theme artefact, not part of the `Node` tree or the wire format** – it travels alongside the tree, preserving the "semantic, not CSS" posture. `FSharp.Core`-only + Fable-portable (the package ships its own dependency-free JSON parser; no `System.Text.Json`, no external JSON dependency).

### `Fuaran.UI.Telemetry.Abstractions`
- `OpApplyTelemetry`, `DenyTelemetry`, `OpKind`, `CacheStatTelemetry` records.
- The `IFuaranTelemetrySink` interface. Adding a new abstract member (Phase 183 added `RecordCacheStat`, after Phase 171's `RecordProviderCall`) is an **additive pre-1.0 minor** event on this still-stabilising contract – direct implementers add `member _.RecordCacheStat _ = ()` alongside their existing members. It becomes a breaking change once `Fuaran.UI` ships `1.0.0`.

### `Fuaran.UI.Memo`
- The incremental re-derivation engine `Engine<'Msg>` (`Apply` / `Reapply`) + the `Derivation<'Msg>` record (Phase 183) – effect-aware memoisation over `FragmentApply.apply`. Transparent: no wire change, no change to apply semantics.
- `FragmentMemo.isCacheable` (the effect-class cache gate) + `FragmentMemo.BoundedLru` live in `Fuaran.UI` (Fable-clean, reusable). `FragmentKey.*` (the canonical-JSON-keyed content hash) lives here.

### Wire format

The canonical JSON wire format – the serialisation of a `Node` tree / `TreeOp` produced by `CanonicalJson.encodeNode` / `encodeOp` and consumed by `Fuaran.UI.Ops.JsonDecode.decodeNode` / `decodeOp` – is specified language-neutrally in [`docs/WIRE_FORMAT.md`](docs/WIRE_FORMAT.md), with the [`wire-format-fixtures/`](../wire-format-fixtures/) corpus at the workspace root (94 fixtures + `manifest.json`: 55 Node round-trips + 11 TreeOp round-trips + 28 reject cases) as the executable conformance suite. F# is **one** conformant host of the contract; the spec + corpus, not the F# code, are the authority (Phase 73).

The wire format is **stable**. The following are breaking changes (major-version events, not silent encoder bumps):

- Changing a `"$type"` discriminator string (e.g. renaming `Static` / `EditNode`).
- Removing a field that has been emitted, or changing its JSON kind.
- Changing a sentinel string (`"<closure>"`, `"<opaque>"`, `"NaN"` / `"Infinity"` / `"-Infinity"`).
- Changing a `DecodeError` code string or the six-code set, or the canonical key-ordering / number / string rules in `WIRE_FORMAT.md` §2.

**Non-breaking** (additive, on the same Pre-1.0 minor axis as the rest of the §4b contract): adding a new `NodeKind` / `Spec` / `TreeOp` / `Binding` / `Action` case, or a new optional field that is omitted when absent (decoders ignore unknown keys per the object-key-order-tolerance rule). Per the **forward-coupling rule** ([`docs/WIRE_FORMAT.md`](docs/WIRE_FORMAT.md) §11), any such addition updates encoder + decoder + the `wire-format-fixtures/` corpus – and, once Wave 9 ships, the TS encoder/decoder – in the **same commit**.

#### Wire versioning + forward-compatibility (§15)

The version/profile negotiation layer ([`docs/WIRE_FORMAT.md`](docs/WIRE_FORMAT.md) §15) is a **stable** part of the wire format. Its host-neutral substrate is [`Fuaran.Core.Wire.Versioning`](../../Fuaran-Core/src/Fuaran.Core.Wire/Wire.fs) (see [`Fuaran-Core/STABILITY.md`](../../Fuaran-Core/STABILITY.md)); the TypeScript host re-implements it in `@fuaran-ui/ops`, and both are conformance-corpus-certified (the `envelope-round-trip` / `envelope-reject` fixture families). The stable surface:

- **The reserved envelope keys `$profile` / `$payload` / `$requiredProfile`** (§15.1, reserved per `WIRE_FORMAT.md` §2.1) – the `$`-prefix + canonical key order are load-bearing (a reserved key must sort before any data key). Renaming one, or reordering them, is a **breaking** change. _(The inline profile-requirement key was `requiredProfile` as shipped by Phase 319; the Phase-404 reservation decision renames it to `$requiredProfile` – the wire migration lands with the `envelope-*` fixtures of Phase 403, per `WIRE_FORMAT.md` §15.1.)_
- **The transport-only `Unknown` carrier** `{ kind, payload, requiredProfile }` (§15.3) – the must-ignore-but-preserve contract: a `Behind` consumer preserves an unknown kind's bytes verbatim. Changing the carrier shape, or dropping the byte-for-byte preservation, is breaking.
- **The `negotiate` outcomes** `Current` / `Behind` / `Foreign` (§15.2) and the name+major-equality rule – a consumer MUST hard-refuse a `Foreign` profile and tolerate a `Behind` one. Changing an outcome classification is breaking.
- **The sentinel / label strings** – the profile-id grammar `<name>@<major>.<minor>`, the base profile `core@1.0`, the `$requiredProfile` key (§15.1), and the `FOREIGN_PROFILE` refusal code are stable identifiers; changing one is a breaking change (same footing as a `DecodeError` code).

The §15 layer is **additive to a `core@1.0` artifact** – the bare (un-enveloped) form is unchanged and read as the implicit `core@1.0` profile, so every existing fixture is byte-unchanged. Bumping the wire **minor** (a new additive kind/case/field) stays non-breaking for older consumers (they tolerate it via §15.3); bumping the **major** (a removal/rename) is the breaking `/vN/` boundary an older consumer refuses as `Foreign`.

## Unstable surfaces

The following are explicitly **not** covered by semver and may change in any patch release without notice:

- Anything in a module / namespace named `Internal` (e.g. `Fuaran.UI.Internal.*`).
- Anything in a member name prefixed `__` (double underscore).
- Anything in a module / namespace named `Private` or `Detail`.
- `Fuaran.UI.Tests` and any other `*.Tests` project – test-only surface, no consumer guarantee.
- Concrete OpStream sinks (`Fuaran.UI.OpStream.InMemory`, `Fuaran.UI.OpStream.Sqlite`, `Fuaran.UI.OpStream.Replay`) – implementation details; the contract is the Abstractions package.
- The concrete observer implementations in `Fuaran.UI.LayoutObserver` – `InMemoryLayoutObserver` and `BrowserLayoutObserver` are implementation details.
- The concrete observer implementations in `Fuaran.UI.StyleObserver` – `InMemoryStyleObserver` and `BrowserStyleObserver` are implementation details; the contract is the Abstractions package.
- The concrete telemetry sinks in `Fuaran.UI.Telemetry.Default` and `Fuaran.UI.Telemetry.Drift` – implementation details.
- `Fuaran.UI.Renderer.DebugGlobal`, `.ChangeHub`, and `.Relay` – the debug-only in-page introspection surface (`window.__fuaran`), the committed-tree-change hub behind it, and the DevTools relay page peer. All three are registered/installed **only** under a DEBUG build with an explicit host opt-in, so they are `undefined` / absent in production; do not build features against their F# shape.

  **But the relay's own stability contract is the `relay@1.0` profile and its conformance corpus, not this package's semver.** Those are different axes and the profile is the binding one: adding a request type, a capability, an optional payload field, or a refusal class is a **minor** relay bump; removing or renaming any of them is a **major** one; and a change to the profile *id* breaks every peer regardless of what `Fuaran.UI.Renderer`'s version does. A host may advance its wire profile (`core@1.0`) without advancing its relay profile, and the reverse. See `DEVTOOLS_RELAY.md` §5.3 in the wire-format specification repository.

  Phase 739 additions to `DebugGlobal.ApplyOutcome` (`AppliedWithTree` / `DecodeFailedWith` / `RejectedWith`) are **additive cases**: an existing host's construction sites are unchanged, and a host that stays on `Applied` / `DecodeFailed` / `Rejected` keeps working — it merely forgoes the exact-revision guarantee and the typed refusal detail the new cases carry.

## Worked examples of breaking-vs-non-breaking

| Change | Classification | Why |
|---|---|---|
| Add a new field to `Defaults.Button` | **Minor** | Consumers re-use `Defaults.Button` and inherit the new field automatically. |
| Add a new field to `Theme` | **Minor** | Consumers compose `{ Defaults.theme with ... }` and inherit the new field automatically. |
| Add a new `--fuaran-*` variable declaration to `fuaran-reference.css` | **Minor** | The reference CSS is the documented contract; new declarations are additive. |
| Add a new `NodeKind` DU case | **Minor** | Existing consumers compile with a `FS0025` exhaustiveness warning. |
| Change `Fuaran.metric "id" { Value = ... }` smart-constructor parameters | **Major** | Existing call sites stop compiling. |
| Change the `JsonEncode` shape of `Action<'Msg>` | **Major** | Wire-format change; consumers persisting / transporting `Action` payloads break. |
| Rename `Internal.SnapshotProjector` to `Internal.StateProjector` | **Patch** | Internal namespace; no consumer guarantee. |

## Recorded breaking change — 0.4.0, `InsertChild` / `MoveNode` lose their position

`TreeOp.InsertChild` and `TreeOp.MoveNode` no longer carry an integer position. Both **append**;
`ReorderChildren` states order by naming node ids. Placing a node anywhere but last is
`Batch [InsertChild …, ReorderChildren …]`.

```fsharp
// 0.3.x
| InsertChild of parentId: NodeId * position: int * child: Node<'Msg>
| MoveNode of NodeId * newParentId: NodeId * newPosition: int

// 0.4.0
| InsertChild of parentId: NodeId * child: Node<'Msg>
| MoveNode of NodeId * newParentId: NodeId
```

**Major by both tests in the table above** — the DU case arities change, so pattern matches and
construction sites stop compiling; and the wire shape changes, so persisted `TreeOp` payloads are
affected. Carries `fuaran-core#95` onto the wire (`SkeletonOp` made the same removal in
`Fuaran.Core.Ops` 0.2.0).

**Why, in one line:** where a collection's members have identity, they are addressed by it — every
node has an id, every other op addresses by one, and `ReorderChildren` already stated order that way,
so the ordinal was the single place the structural surface departed from the tree's own identity
model. It also named something the tree does not store, since children are a list and order is
structural; an index is a projection over that list, valid only against one snapshot of it.

**The rule this sets, for future surface changes:** an ordinal must not be reintroduced to address a
collection whose members have identity. It stays legitimate exactly where identity is absent —
`Columns[i]`, `Fields[i]`, `TabHeaders[i]` and the other bounded payload collections inside a single
node are contained data, not tree structure, and are unaffected.

**Migration.** A `0.3.x` emission still decodes: the decoder accepts and ignores a legacy
`position` / `newPosition`, applying the op as an append. That is a migration mechanism for the
hosts adopting independently, not a supported authoring form — the encoder never writes the field,
and the tolerance is removed once every host is positionless (`fuaran#687`).

## Recorded breaking change — 0.5.0, the opaque correlation slot + the validate telemetry leg

Phase 330's public half. Two changes, both requiring consumer edits.

`RenderContext` gains a required `SessionContext: Map<string, string>` — the host's opaque
correlation slot, read per render and stamped onto `RenderFailureTelemetry` by
`emitRenderFailureWithContext` via the well-known keys `Render.promptIdKey` / `Render.userIdKey`.

```fsharp
// 0.4.0
{ Sources = …; Sink = … }

// 0.5.0 — every RenderContext literal names the new field
{ Sources = …; Sink = …; SessionContext = Map.empty }
```

Deliberately a **map, not a typed record**. The renderer does not know, and must not know, what a
host's ids mean — it threads and stamps them; a typed `{ PromptId; TurnId; UserId }` would publish a
host's correlation design on a public surface, and a map cannot by construction. Precedent:
`Node.ExtraAttributes`. The slot is never hashed and never on the wire. A `Mount` guest inherits the
host's context, so a failure inside a mounted region belongs to the interaction that mounted it. The
node-hash `CorrelationId` stays alongside as the intra-frame disambiguator — a different job.

`IFuaranTelemetrySink` gains a **sixth member** for the validate leg, with the new
`ValidateOutcomeTelemetry` record and `ValidateOutcome` DU (`Clean` / `Warnings` / `Errors` /
`NotRun`). Net-new rather than a wiring fix: `Fuaran.UI.Validator` is a build-time AST walker over
source and cannot see a tree produced at runtime, so no validate leg existed to wire. `NotRun`
carries a reason but **no count** — an aggregate reading "we could not check" as "zero findings"
would report a broken validator as a clean session.

**Major by the first test in the table above** — a required record field and an added interface
member both stop consumer code compiling. Migration is mechanical: add `SessionContext = Map.empty`
to each `RenderContext` literal (which reproduces 0.4.0 behaviour exactly), and implement the sixth
sink member. The pre-330 `emitRenderFailure` arity survives as a `Map.empty` wrapper, so existing
call sites of *that* function are unchanged.

## Recorded change — 0.6.0, correlation-aware render entry + validator corrections

**Additive on the renderer.** `renderWithSourcesSinkAndContext` is `renderWithSourcesAndSink` plus
the host's `SessionContext` — a separate entry point rather than a parameter on the existing one, so
a host with no correlation context never writes `Map.empty` at a call site that never had the
concept. The context is a **value read once for this render**: a host that captures one at
construction and reuses it freezes the first interaction's id onto every later frame's telemetry.

**`Fuaran.UI.Validator` behaviour changes.** Two false-positive classes removed and one new opt-out.
All three change what an existing project's `Validate` run reports, so they are called out here even
though no type signature moved:

- **FUARAN001 no longer fires across two trees that share a root id.** Per-tree NodeId uniqueness was
  grouped by the root's *id string*, so two `Fuaran.dashboard "doc"` invocations were treated as one
  tree and every id they legitimately shared was reported as an intra-tree duplicate. A tree is now
  identified by its root **call site**. Cross-tree sharing was always meant to be the FUARAN002
  warning, and now is. Strictly a false-positive reduction — a genuine duplicate inside one tree
  still errors.
- **FUARAN044 accepts `FormFieldKind.RangedNumber`.** The renderer mounts the per-NodeId `useState`
  buffer for three field kinds; the rule recognised two, so a `binding.local` inside a
  `RangedNumber` field was a false error on correct code. The recognised set must track `Render.fs`'s
  `Binding.Local` arms.
- **Source-level suppression pragmas** — `// fuaran-validator: disable <CODES>` (file-scoped) and
  `// fuaran-validator: disable-next-line <CODES>`. For source that deliberately holds a rejected
  shape, canonically a negative test asserting the runtime reports the defect. Only the reporting
  layer is filtered — every check still runs — and suppressed findings are counted in the run summary
  rather than hidden. `Validator.RunResult` gains a `Suppressed: int` field, which is a recompile for
  any consumer constructing that record directly (the CLI is the expected consumption surface). See
  the [validator README](src/Fuaran.UI.Validator/README.md#suppressing-a-finding).

## Recorded change — 0.7.0, `FormFieldKind.DateRange`

**Additive wire vocabulary.** `FormFieldKind` gains a `DateRange` case — the single-control date
range (`value: Binding<string * string>` carrying the ordered ISO-8601 `(from, to)` pair, plus the
`DateVariant` and the `DateFieldConstraints` that bound both ends). No existing emission changes
meaning: a tree with no `DateRange` field encodes to exactly the bytes it did at 0.6.0.

**What it costs a consumer.** Per the [Semver](#semver) note above, a new DU case is *minor*, not
major — but it is a new case in a widely-matched DU, so any consumer with an exhaustive `match` over
`FormFieldKind` gets `FS0025`, which is a build break under `TreatWarningsAsErrors`. Sites to expect
in a renderer or analysis pass: the form-field control arm, the filter-chip control arm, binding
walks, and any write-back/inertness classification.

**Wire contract.** `{"$type":"DateRange","onChange"?:"<closure>","value":<Binding<string*string>>,
"variant":"Date"|"Time"|"DateTime","min"?:<iso>,"max"?:<iso>,"step"?:<number>}`. The `Static` pair
rides as the bare `{"from":…,"to":…}` object — the `FormFieldKind.Range` posture, no `Static`
envelope — with the `[from,to]` array and the enveloped form decode-accepted. A **literal** pair must
be ordered (`from <= to`, ordinal compare); a reversed one is a `WRONG_TYPE` decode error naming the
rule. Corpus: `nodes/form-date-range.json`, `nodes/filters-date-range.json`,
`reject/reject-daterange-unordered.json`, `lenient/lenient-daterange-{bare-array,static-envelope}.json`.

**Host adoption.** The F# reference and the shared corpus land together; the other conformant hosts
follow by fixture (their decode legs are filed as their own phases).

## Recorded change — 0.8.0, the swap onto the IDL-generated types (Phases 692–694)

**One version covers the whole swap**, not one per stage: the staged execution was a green-gate
convenience, the contract change is a single event.

**What changed.** `src/Fuaran.UI/Types.fs` no longer *defines* the wire vocabulary — it
*abbreviates* `Fuaran.UI.Generated`, the layer emitted from the IDL in the substrate sibling.
`Node<'Msg>`, `NodeKind<'Msg>`, `Binding<'T>`, `Action<'Msg>`, `TextSource`, `FormFieldKind<'Msg>`,
every spec record, and the display enums are now type abbreviations over the generated types. The
hand-written per-kind node encoder in `Fuaran.UI.OpStream.Abstractions.CanonicalJson` is **deleted**
(2,351 → 632 lines); the generated encoder is the encoder, and the op codec splices its renderings.
`CanonicalJson.encodeNode` survives as the public entry point.

**The wire did not move.** Every corpus fixture is byte-identical: a full `--emit-corpus`
regeneration at the landing reproduced all 242 fixtures + 39 kinds + `schema.json` with a zero diff.
This is an internal type-model change, and the canonical bytes are the proof.

**What it costs a consumer.** Source edits, not behavioural ones, and they bite *record literals and
match arms*, not the authoring surface:

- **Structurally identical, nominally different.** Field order within each union case was aligned to
  the hand-written positional order before the swap, so construction and match sites compile
  unchanged wherever arity and payload shape already matched.
- **Typed absence.** Control `value` slots are `Binding<'v> option` and `Binding.Static` payloads are
  option-wrapped (`Binding.Static(Some x)`); `Node.State` / `Node.Style` are `option` (absence is
  structural). Previously-required closure slots are `option`.
- **Wrapper erasure.** `Node.Id` is a bare `string` (`NodeId` survives as the ops / store / tool
  wrapper); `ApiEndpoint` likewise erases at the tree level. A wrapper carrying runtime state keeps a
  host-only slot instead (`FileRef.Handle`).
- **Pair records.** `Range`'s `float * float` is the `RangePair` record and `DateRange`'s
  `string * string` is the `DateRangePair` record — the record IS the wire object.
- **Positional reshapes.** `LayoutMode` replaces the nested `BoxLayout`/`FlexLayout`/`GridTemplate`
  records; `NumberFieldConstraints` / `DateFieldConstraints` are flattened to positional
  `min` / `max` / `step` case fields (both records survive as host-side helpers).

**What it does NOT cost.** The `Fuaran.*` smart-constructor authoring surface keeps its signatures.
`samples/demo` — a complete authored application built only on that surface — has **zero** source
changes across the entire swap.

**Why it is worth the bump.** Adding a kind is now **1 IDL entry + 13 compiler-forced tier files**,
where it was those 13 plus the hand-written type definition plus the encoder mirror; the
hand-maintained line count fell by 1,530 net. `WIRE_FORMAT.md` §11 step 1 names the IDL as the single
source for the F# structural layer.

## Recorded change — 0.9.0, the scope × sink render entry + the guest capability seam

**Additive on `Fuaran.UI.Renderer`. No consumer source edits, no wire change, no
behavioural change for a host that adopts neither addition.** Both close gaps the
first Custom-node consumer hosts surfaced, and both sat on the *raw render path* —
the path an orchestration tier was wrongly assumed to be covering.

**`renderWithSourcesInScopeAndSink`** — the render-entry matrix had no cell
combining a runtime **scope** with a telemetry **sink**. `renderWithSourcesInScope`
hard-coded `TelemetrySink = None`, so a scope-aware host silently lost every
render-failure event as the price of state isolation, while every sink-carrying
entry rendered against the process-global `StateStore`. The two axes are
orthogonal and no entry should have made a host trade one for the other — least
of all this one, since the host most likely to need isolation (mounted guests,
registered custom renderers) is the host most likely to render trees it did not
author. Scoping semantics are identical to `renderWithSourcesInScope`; sink
semantics identical to `renderWithSourcesAndSink`.

**`GuestSeam`** — the `Mount` arm handed the guest the **host** runtime
(`guestCtx.Runtime = ctx.Runtime`) and bridged its dispatch through the mount's
`OnBubble` **unwrapped**, so the capability gate was enforced only where the
orchestration tier authored the mount or drove the guest's dispatch loop. A
*rendered* guest was therefore not default-deny. The gate now arrives as a
language-tier-resident hook a host installs — the language tier must not
reference the orchestration tier that owns the policy — mirroring the renderer's
existing late-bound `renderGuestHook`.

**Default-off, deliberately.** `currentGuestSeam ()` is `None` until a host calls
`installGuestSeam`, and the `Mount` arm then behaves exactly as it did at 0.8.0.
This is a capability *seam*, not a capability *change*: shipping a new default
policy in a minor version would silently break every host that mounts a trusted
guest today. The policy decision stays with the host; what shipped is the place
to put it.

**Migration:** none. Adopt `renderWithSourcesInScopeAndSink` where you currently
call `renderWithSourcesInScope` and want telemetry; call `installGuestSeam` where
you want rendered guests gated. See [`docs/RENDER-ENTRIES.md`](docs/RENDER-ENTRIES.md)
for the entry × runtime × scope × sink grid and the `GuestSeam` shape.

**Also recorded here because it is a stability claim about a *non*-change:** the
"passing a telemetry sink trips a Fable assembly-identity split under source-packed
`PackageReference` consumption" trap does not reproduce (verified 2026-07-08, Fable 5,
concrete `IFuaranTelemetrySink` object expression through `renderWithSourcesAndSink`).
It was never a documented constraint of this package and is not one now. The
authoritative hosting story is `docs/RENDER-ENTRIES.md`.

## Recorded change — 0.10.0, DAG checkpoint verification + the op-stream decode leg

**Minor.** Five changes across `Fuaran.UI.OpStream.*`, two of which read as major at a glance. The
axis is argued against the policy text below rather than assumed, because the argument is the
useful part.

**The canonical wire specification did not move — but one non-conformant emit path was corrected.**
`StreamEntry.encode` is untouched and `chainFormatVersion` stays `2`; the `DagOpRecord` pre-image is
untouched; no `wire-format-fixtures/` fixture changed, and the corpus-backed JsonDecode conformance
suite is green. None of the [Wire format](#wire-format) major events applies. **The one exception is
§5 below**, where `TreeOp.UpdateState` had been emitting bytes the spec never permitted; correcting
it changes those bytes, and §5 states the one consequence that follows for persisted chains. Record
`Hash` values are byte-identical for every chain that does not contain such an op.

### 1. `Fuaran.UI.OpStream.Abstractions` gains three functions — additive

`StreamEntry.decode` (the inverse of the pinned provenance envelope, previously an explicit
`Error "…"` placeholder, so the `StreamWitness` contract was only two-thirds satisfied),
`StreamEntry.ofCoreRecord`, and `coreWitnessWith <decodeOp>` (the full witness, alongside the
existing verify-path `coreWitness`, whose `Decode` now refuses by name and points at the decoding
one). No existing signature or wire shape changes. **Minor** by the Semver rule — *backward-compatible
feature addition; existing consumer code keeps compiling*. This is the change that makes the release
a minor rather than the patch a behavioural tightening alone would take.

The decoder is a **top-level field scanner**, not a flat search, and that is load-bearing: the
envelope's `op` is arbitrary canonical JSON that legitimately contains its own `ts` / `result` /
`promptId` keys, so a whole-string search would read an envelope field out of the nested payload.
The format version is **checked**, not merely readable — an unknown `v`, and the tagless pre-406 v1
form, are refused by name.

### 2. `DagReplayError` gains `SnapshotHashMismatch` — a new DU case, still minor

A new case in a matched DU, so any consumer with an exhaustive `match` gets `FS0025`, which **is** a
build break under `TreatWarningsAsErrors`. It is nonetheless **minor**, and the policy says so twice:
the [Semver](#semver) section — *"DU exhaustiveness warnings (F#'s `FS0025`) are not considered
breaking… existing consumers compile with a warning that they don't handle the new case, which is
the correct signal"* — and the worked-examples table's *"Add a new `NodeKind` DU case | **Minor**"*.
The [0.7.0 entry](#recorded-change--070-formfieldkinddaterange) settled this exact shape, naming the
`TreatWarningsAsErrors` break explicitly and classifying it minor anyway. Consumers matching
`DagReplayError` exhaustively add the arm.

### 3. `replayFromCheckpoint` verifies `SnapshotHash` — a behavioural tightening

**This was the security defect.** `DagReplay.replayFromCheckpoint` trusted `checkpoint.Snapshot`
outright and **never looked at `SnapshotHash` at all**, so materialising a checkpoint was silently
unchecked — replay folded the tail over whatever tree it was handed. And `SnapshotHash` itself bound
only the tree, not the position (the A2 hole the linear chain closed at Phase 412), so a genuine
snapshot from one node validated unchanged when presented as the checkpoint for a *different* one.

Both halves are closed, mirroring `HashChain.snapshotHash` with the DAG's content-addressed node
standing in for `(previousChainHead, sequence)`: a new `DagCheckpoint` module (`snapshotHash`,
position-bound; `create`, so the binding cannot be forgotten at a record literal;
`verifySnapshotHash`), and `replayFromCheckpoint` verifying **before** folding.

**A tightening of the 0.3.3 class** — input that was accepted is now refused, and that refusal is the
guarantee working rather than a regression. Same footing as the `Sanitize` policy note above, which
puts *"rejecting … previously accepted"* on the minor axis. No durable-data migration cost: no sink
persists a `DagCheckpoint`, so nothing on disk carries the old formula.

`DagCheckpoint` is an addition to the stable `Fuaran.UI.OpStream.Dag.Abstractions` surface;
`DagVerify` / `GuestFork` additionally gain Fable availability (the `#if !FABLE_COMPILER` fences are
gone, so a browser host can verify a DAG), and `DagVerify.records` is rewritten over `Map`, which
makes "the first violation" deterministic across hosts where the `Dictionary` it replaced iterated in
insertion order.

### 4. `GuestExport.FormatVersion` 1 → 2, v1 documents refused — below the semver line

The bundle **document** envelope moved (`GuestExport` JSONL is now `Core.OpStream.toJsonl` /
`fromJsonl` over the witness; the hand-rolled `OpRecordWire` envelope re-specified a format Core
already owned, with a *different* field vocabulary from the chain pre-image the same records hash
under, and is deleted). A v1 document is refused by name, which is what that field is for.

Refusing previously-accepted documents would ordinarily be the strongest case for a major bump here.
It is not, because `GuestExport` lives in **`Fuaran.UI.OpStream.Replay`**, which
[Unstable surfaces](#unstable-surfaces) declares an implementation detail — *"the contract is the
Abstractions package"* — explicitly *"not covered by semver"* and changeable *"in any patch release
without notice"*. So the document-format bump carries no semver promise and cannot drive the axis up.
It is recorded here anyway because a consumer holding v1 guest bundles must re-export them, and
"no promise" is not the same as "no consequence".

### 5. `CanonicalJson.encodeOp` — `TreeOp.UpdateState` now emits its node payloads in canonical form

`stateBehaviourAppender` spliced the raw `StateBehaviour` record into the structural encoder, while
its two siblings (`nodeAppender`, `nodeKindAppender`) route through `Introspect.canonicalForm` /
`canonicalFormKind`. Since `StateBehaviour` carries `onLoading` / `onEmpty` **Node** payloads, this
one op path bypassed the canonical-form projection — so a state node carrying an explicit auto
binding (a `Filters` chip whose control declared `Filter(<own name>)`) emitted a `value` key that
[`WIRE_FORMAT.md`](../wire-format-fixtures/WIRE_FORMAT.md) **requires the encoder to omit**.

**Not a wire-format change:** the spec always required the omitted form, and F# was non-conformant on
this path alone. Decode is unaffected — both forms decode to the same tree — so **no persisted
document becomes unreadable.**

**The one consequence worth pinning:** canonical bytes feed the op-stream hash chain, so a chain
persisted before this fix that *contains* such an `UpdateState` will re-hash differently and fail
verification if re-encoded. Chains without one are byte-unchanged.

The fix maps `canonicalForm` over the two Node slots — deliberately **not** the `canonicalFormKind`
scratch-envelope shape, because `canonicalForm` collapses an all-`None` state to absence, which is
right for a node's own `State` field and wrong for an op that explicitly *sets* one. `OnError` is
`ErrorPayload -> Node`, so there is no node to project until applied.

**Found by the cross-host fuzz exchange on its first run** (Legs F/G, 2026-07-29): 4 divergences per
600 samples, every one an `UpdateState`, while the converse Python→F# leg was 600/600 — which
isolated the fault to encode-side canonicalisation rather than the codec. Verified fixed across six
fresh draws × 600 = 3,600 samples with zero divergences, and pinned deterministically by
`CanonicalFormOpTests.fs` (three of its seven tests failed against the unfixed encoder; one is a
negative case — a chip bound to a *different* filter must keep its `value`, so "canonicalise" cannot
be satisfied by deleting every value).

A survey of the remaining appenders confirms the pattern is now **complete** rather than
two-of-three: `SemanticStyle` is bare enum fields, `PropValue` is a scalar or already-canonical
`Wire` JVal, and `Binding` / `Action` resolve to values and messages — none can carry a `Node`. That
invariant is recorded at the appender block so a future Node-bearing payload has a rule to meet.

**Consumer impact:** repin; add the `DagReplayError` arm if you match it exhaustively; re-export any
v1 guest bundle; and if you persist op-stream chains containing `UpdateState` ops with state-node
payloads, re-verify them (§5). A consumer that uses neither the DAG packages nor `GuestExport` sees
an additive release only.

## How this policy interacts with phase authoring

Per the workspace roadmap conventions, every phase that proposes a change to a stable surface (per the table above) must flag it explicitly in its phase body:

```
**Stability impact:** Breaking change to `Fuaran.UI` (changes smart-constructor signature for `Fuaran.metric`). Requires major-version bump on next release.
```

Roadmap maintenance passes scan for this annotation and surface major-version-bump candidates in their summaries.

## Recorded change — 0.11.0, `CellKindErased.TonedPill`

**Additive wire vocabulary.** `CellKindErased` gains a `TonedPill` case — a grid cell whose tone is
a *declared* value→tone mapping rather than a host closure: `field` (the row property that supplies
both the pill's label and the map key), `map` (`Map<string, ToneVariant>`), and `default` (the tone
for a value the map does not mention, omitted on the wire at `ToneVariant.Default`). The typed author
facade `CellKind` gains the same case, `Column.withTonedPill` is its smart constructor, and the C#
`Column<TRow>` facade gains `WithTonedPill`.

**Why it exists, in one line:** `Pill`'s two fields are both closures, so on the wire it is
`{"labelFn":"<closure>","toneFn":"<closure>"}` and a value-conditional tone was **inexpressible** —
not verbose, inexpressible, since no lenient shape can conjure a function. It is the one entry in
`WireSurvivability` whose "recoverable alternative" was honestly `–` until now.

**No existing emission changes meaning or bytes.** A tree with no `TonedPill` cell encodes to exactly
the bytes it did at 0.10.0; the full `--emit-corpus` regeneration touched only the five new fixtures
and the schema's new `$defs.CellKindErased` branch. `Pill` is untouched and remains the override for
a tone that genuinely needs host computation (a threshold against live data, a cross-field predicate).

**What it costs a consumer.** Per the [Semver](#semver) note, a new DU case is *minor*, not major —
but `CellKindErased` is a matched DU, so an exhaustive `match` gets `FS0025`, a build break under
`TreatWarningsAsErrors`. This is the axis [0.7.0](#recorded-change--070-formfieldkinddaterange)
settled. Sites to expect: a grid cell renderer arm, an `AgGrid`-style column-def builder, a
`'Msg`-mapping walk, and any survivability / inertness classification. The count is small and
knowable — six files in this repo, all compiler-forced.

**Wire contract.**
`{"$type":"TonedPill","default"?:<ToneVariant>,"field":<string>,"map":{<string>:<ToneVariant>}}`.
`map` values are the ordinary `ToneVariant` vocabulary and accept the §3.6 aliases
(`Danger`→`Critical`, `Positive`→`Success`, `Neutral`→`Default`); an unrecognised value is an
`UNKNOWN_DU_CASE` decode error naming all seven legal tones, reported at the offending **key**
(`$…map.Delayed`). Two §16 shorthands are accepted: `toneMap` / `tones` alias `map`, and a
`{"$type":"Pill"}` cell **carrying** a tone map normalises to `TonedPill` — that second one is not a
convenience but a data-loss fix, since before 0.11.0 those keys were accepted and silently discarded.
Corpus: `nodes/grid-toned-pill.json`, `reject/reject-tonedpill-unknown-tone.json`,
`lenient/lenient-tonedpill-{pill-tag,tonemap-alias,tone-aliases}.json`.

**Renderer.** Both grid backends (the simple-table cell and the AG Grid cell renderer) route through
one shared lowering, `BindingResolver.tonedPillOf`, and emit the *same* element, class vocabulary and
text as the hosted `Pill` arm — the case exists to make the rule expressible, not to render
differently. A parity test pins the two derivations against each other over the cases a lookup gets
wrong (mapped, unmapped, empty, field-absent, non-Map row). SSR is unaffected: it renders a
hydration placeholder for a data-bound grid, never per-cell markup.

**Host adoption.** The F# reference, the TypeScript tier and the shared corpus land together; the
Python, Go and Rust codec legs follow by fixture as their own phases (the 725 / 730–733 precedent).

## Versioning policy (release identifiers)

The `<Version>` property in [`Directory.Build.props`](Directory.Build.props) governs the package id+version pair pushed to **nuget.org**, the sole released channel — public and no-auth-restore. Policy: **per-release semver bump**.

> **A version is released only when a `v*` tag has been pushed and the packages are visible on nuget.org.** Bumping `<Version>` does not release anything; it only claims the next slot. Check the registry, not the last workflow run — 0.5.0 was bumped past on the belief it had shipped, when in fact no `v0.5.0` tag was ever pushed and 0.5.0 exists nowhere but one machine's local feed. **nuget.org is permanent**: a published version can be unlisted but never deleted or replaced.

- Pre-1.0: `0.0.1-alpha.N` form (e.g. `0.0.1-alpha.1` → `0.0.1-alpha.2`). Each release bumps the trailing `N`. Annotate the bump in the commit body when it lands.
- Post-1.0: standard semver per the "Semver" section above (major / minor / patch).
- Every CI publish-run produces a new id+version pair. GH Packages refuses re-push of the same pair, so the version must move forward before each tag push that triggers the publish workflow.

Rationale (per `../workspace/docs/github-packages-mirror-from-forge.md`):
- Human-readable in consumer `Directory.Packages.props` pins.
- Retention policies in GH Packages prune by version pattern; semver is well-understood.
- The tag-push trigger naturally encourages bumping `<Version>` before tagging, so tag, package version, and changelog all agree.

Re-runs of an already-published version are safe because the publish workflow uses `--skip-duplicate` (idempotent push). A failed publish attempt that committed a version bump should bump again rather than reuse the failed version – keep the audit trail linear.

## Re-confirmation gate before public exposure

Before this repo flips public (whether as a public GitHub repository, a published-to-nuget.org package, or surfaced in marketing material), the licensing posture declared in [`LICENSE`](LICENSE) must be re-confirmed by Diametrical Ltd.

## See also

- [`docs/VOCABULARY.md`](docs/VOCABULARY.md) – the vocabulary-growth governance charter (admission /
  merge / retirement of a `NodeKind`; the plateau + confusion guard; post-publication wire-profile
  versioning for kind additions)
- [`LICENSE`](LICENSE)
- [`CONTRIBUTING.md`](CONTRIBUTING.md)
- [`CLAUDE.md`](CLAUDE.md)

## Recorded breaking change — 0.12.0, typed row-source encoding (fuaran#665)

**Grid / chart rows leave the residual-`"<opaque>"` boundary.** The rows slot is typed end to end:
`type Row = Fuaran.Core.Row` (= `Map<string, obj>` — the shape the decoded path and
`Binding.Transform` resolution always produced at runtime, so the *representation* is unchanged and
only the signatures move), `GridSpec.Source` / `ChartSpec.Source` are `Binding<Row seq>`, and every
row-consuming closure slot (`ColumnErased.Value`, `RowKey`, `OnRowClick`, `OnPointClick`, the
`CellKindErased` interactive arms, `ButtonGroupItem.OnClick`) retypes `obj` → `Row`.

**API-breaking, in four places:**

- `Fuaran.grid` takes a REQUIRED `toRow: 'row -> Row` projection (operator decision 2026-07-31,
  recorded in the phase file): the author who wants rows on the wire says how to project them, once,
  at the authoring boundary where every `'row` erasure already happened. Not defaultable — the only
  candidate defaults silently produce empty rows (the exact loss the typed slot exists to end) or
  throw. A call site whose rows are already `Row`-shaped passes `id`.
- The typed facade's `Column<'row,'Msg>` / `CellKind<'row,'Msg>` drop their `'row` parameter
  (`Column<'Msg>` / `CellKind<'Msg>`): accessors read the PROJECTED row by name — `'row` survives
  only at `GridSpecOf<'row,'Msg>.Source`. This is forced by the algebra (the original `'row` value
  no longer exists past the projection) and is the direction the declarative floor (Phases
  425/428/750) already established: rows are name-addressable.
- The C# fluent facade mirrors it: `DataGridOptions<TRow>` gains a `required ToRow`, `Column<TRow>`
  becomes the row-type-free `Column` over `IReadOnlyDictionary<string, object>` accessors.
- The renderer's Fable `#if`-split unbox hacks (`BindingResolver.projectRowFieldValue`,
  `Render.updateRowField`) are DELETED — the slot is statically typed, so no runtime row test exists
  to get wrong. This closes the F#/Fable byte-parity hazard the phase's task-1 measurement proved.

**Wire-additive with read-compat, plus a deliberate corpus advance:** a Static/State rows payload
now encodes as a JSON array of row objects (scalar cells per WIRE_FORMAT §2 rules 5/11, rendered by
`Fuaran.Core.RowCodec` — Core 0.2.1); the legacy `"<opaque>"` sentinel stays decode-accepted
INDEFINITELY (→ the empty feed), pinned by the `lenient-665-rows-opaque-sentinel` corpus fixture.
The corpus gains `grid-editable-state` + `chart-state-rows` (the Phase 663 editable write-back
anchor — `editable: true` + a direct `$state` rows source, previously uncertifiable because rows
could not survive encoding). Cross-host codec parity (TS / Python / Go / Rust) is the phase's open
follow-up; until each lands, that host's corpus gate is deliberately red on these fixtures.
