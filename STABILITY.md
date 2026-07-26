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
- The `Sanitize` module's policy contract – `sanitizeExtraAttributes`, `sanitizeUrl` / `sanitizeUrlOrBlank`, `sanitizeMarkdownHtml` (Phase 56). Tightening the policy (rejecting an attribute key or URL scheme previously accepted) is a behavioural change to renderer output and counts as a minor-version bump; loosening it (accepting an attribute key or URL scheme previously rejected) is additive. The injection-safety contract is documented separately in [`SANITIZATION.md`](SANITIZATION.md), which the renderer leans on as the source of truth for which inputs are neutralised at render time.

### `Fuaran.UI.Ops`
- The `TreeOp<'Msg>` DU (the §4g op vocabulary).
- The `ApplyError` / `ApplyErrorCode` / `ApplyHint` records (the §4d AI-recovery error shape).
- The `Apply.apply : TreeOp<'Msg> -> Node<'Msg> -> Result<Node<'Msg>, ApplyError>` entry point.
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

## How this policy interacts with phase authoring

Per the workspace roadmap conventions, every phase that proposes a change to a stable surface (per the table above) must flag it explicitly in its phase body:

```
**Stability impact:** Breaking change to `Fuaran.UI` (changes smart-constructor signature for `Fuaran.metric`). Requires major-version bump on next release.
```

Roadmap maintenance passes scan for this annotation and surface major-version-bump candidates in their summaries.

## Versioning policy (release identifiers)

The `<Version>` property in [`Directory.Build.props`](Directory.Build.props) governs the package id+version pair pushed to the GitHub Packages NuGet feed. Policy: **per-release semver bump**.

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
