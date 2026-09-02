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
- fuaran-core Phase 707 – **`LiveRegionKind` moves from `HostPrelude` into the IDL-generated layer. Not a breaking change to any stable surface.** The documented type is `Fuaran.UI.LiveRegionKind` (the `Types.fs` alias), and its name, its three `[<RequireQualifiedAccess>]` cases (`Polite` / `Assertive` / `Off`) and its wire strings (`"polite"` / `"assertive"` / `"off"`) are all unchanged — only what the alias points at moved. The slot was `THosted` because an IDL enum's case name USED to be its wire string, which lower-case wire values cannot spell; the IDL now carries a case↔wire mapping, so `accessibility.liveRegion` is a declared `TEnum` and the generated layer owns the DU and its codecs. Consequence for a consumer that reached past the alias: `Fuaran.UI.HostPrelude.LiveRegionKind` / `encLiveRegionKind` / `decLiveRegionKind` no longer exist (`HostPrelude` is plumbing for the generated layer, not a declared stable surface — see its header). `AriaRole` stays `THosted` for a reason no mapping can fix: its `Custom of string` case makes the set genuinely OPEN. **No wire-format impact — the corpus and the generated-layer pins are byte-identical either side of the change**; `WIRE_FORMAT.md` §3's `LiveRegionKind` line already declared the closed set, and the IDL now agrees with it rather than treating the slot as opaque JSON.
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
- **Decision (Phase 119): the renderer owns a dispatch policy-gate seam.** `IFuaranRuntime.CanDispatch : ActionDescriptor -> bool` is consulted by `runAction` before the gated host effects (`Call` / `Navigate` / `AiTool` – Phase 136 adds `ReadFileBody` to the gated set via the additive `ActionDescriptor.ReadFileBody of fileId: string` case); on deny the renderer emits a `Warn` diagnostic and skips the effect. The language tier therefore exposes its **own** default-deny seam rather than deferring the gate strictly to a downstream orchestration tier – a standalone host (e.g. the BYOK browser playground) consuming only the public packages can make the dispatch path default-deny without a §4j orchestration gate in the loop. **INVERTED in 0.14.0 (Phase 782): the default runtimes DENY.** They returned `true` (allow) from Phase 119 until then, which made the gate an opt-in a host had to remember to override — the inverse of the posture the language claims, with the shipped default contradicting the published claim. `Diagnostic` / `Mutable` / `Browser` / `DriverServices.create` / `BoundedServices.create` now refuse every descriptor, and the permissive posture is reached BY NAME: `Runtime.permissive`, `PermissiveRuntime`, `MutableRuntime.Permissive()`, `BrowserRuntime.createPermissive()`, `DriverServices.createPermissive`, `BoundedServices.createPermissive`. 0.14.0 also CLOSED the descriptor set — `Notify` / `SetState` / `WriteToClipboard` / `CommitLocal` reached their substrates with no gate consultation at all before it. Per the established precedent below, adding the `CanDispatch` abstract member was a **pre-1.0 minor add** – direct `IFuaranRuntime` implementers add the member, and one returning `true` unconditionally has written a permissive host deliberately rather than inheriting one silently. (F# interfaces cannot carry a true default implementation, so the new member is technically a recompile for direct implementers; all in-repo implementers were updated in the same change, and there are no cross-sibling direct implementers.)
- **The render-entry family and the axes each one pins.** `render` (the general `RenderContext` entry) plus the convenience entries `renderWithSources` / `renderWithSourcesAndSink` / `renderWithSourcesSinkAndContext` / `renderWithSourcesInScope` / `renderWithSourcesInScopeAndSink`, and the composing wrappers `renderWithTheme` / `renderStateReactive`. **Which `RenderContext` axis an entry PINS is part of the contract, not an implementation detail** – a host picks its entry by exactly that. Adding an entry is additive; changing an existing entry's pinned axis (giving `renderWithSources` a real runtime, say, or pinning a sink where a host supplies one) is a behavioural change to every consumer of that entry and bumps accordingly. The full grid is [`docs/RENDER-ENTRIES.md`](docs/RENDER-ENTRIES.md).
- **`GuestSeam` – the host-pluggable `Mount` guest capability seam.** `GuestSeamContext` (`ScopeId` / `Capabilities` / `Channel`), the `GuestSeam` record (`WrapRuntime` / `GateBubble`), and `installGuestSeam` / `clearGuestSeam` / `currentGuestSeam`. **CHANGED in 0.15.0 (Phase 783), and the previous sentence said this would be breaking — it is.** The no-seam default used to hand the guest the host runtime and an unwrapped bubble; it now hands it a `Runtime.UnprivilegedGuestRuntime` (every capability refused, refusals recorded) and an `OutOnly` channel. A guest is foreign content and `MountSpec.Capabilities` is "a request, not a grant", so a host that installed no policy granting a mounted guest everything the host could do was the inverse of the declared posture. `GuestSeamContext` gains `DeclaredDirection` and `GuestSeam` gains `GrantTwoWay` — both breaking record changes for a host constructing them literally. `TwoWay` is now a host grant; a decoded mount is clamped to `OutOnly` and the downgrade is recorded.
- The `Sanitize` module's policy contract – `sanitizeExtraAttributes`, `sanitizeUrl` / `sanitizeUrlOrBlank`, `sanitizeMarkdownHtml` (Phase 56). Tightening the policy (rejecting an attribute key or URL scheme previously accepted) is a behavioural change to renderer output and counts as a minor-version bump; loosening it (accepting an attribute key or URL scheme previously rejected) is additive. The injection-safety contract is documented separately in [`SANITIZATION.md`](SANITIZATION.md), which the renderer leans on as the source of truth for which inputs are neutralised at render time.
- **The destination policy is AMBIENT as of 0.33.0 (Phase 1026) — BREAKING, on both axes at once.**
  Phase 897 shipped the typed origin allowlist (`Sanitize.EgressPolicy` / `EgressClass` /
  `checkDestination` / `sanitizeUrlForEgress`) and left every emission site calling
  `sanitizeUrlOrBlank`, so the policy decided nothing unless a caller asked. 1026 puts it on the
  record every render already threads, and routes every `href` / `src` / tree-declared route through
  it.
  - **Type-breaking:** `RenderContext<'Msg>`, `ServerRenderContext`, `DriverServices<'Msg>`,
    `Email.EmailOptions` and `FuaranGiraffeOptions` each gain a required `EgressPolicy` field — a
    recompile for any host constructing them literally. `Render.treeNavigate` /
    `treeNavigateOutcome` gain a policy parameter.
  - **Behaviour-breaking, and the larger half:** the default is `Sanitize.denyNonLocalEgress`, so a
    host that declares nothing renders `about:blank#fuaran-egress-refused` — plus a
    `data-fuaran-egress-refused` marker naming the class and the host — wherever a tree pointed at an
    undeclared origin. Same-origin destinations are unaffected: the default denies *leaving*, not
    *linking*. **`mailto:` / `tel:` are refused by default** (`AllowNonNetwork = false`), which is the
    consequence an adopting host meets first.
  - Per the Phase 782 precedent above, the permissive posture is reached BY NAME:
    `Sanitize.permissiveEgress`, `renderWithSourcesAndEgress`, `Render.renderWithEgress`,
    `mkContextWithEgress`, `Hydration.renderWithIslandsAndEgress`, and
    `DriverServices.createPermissive` (which now opens egress alongside the dispatch gate). Full
    adoption guide:
    [`docs/migrations/1026-ambient-destination-policy.md`](docs/migrations/1026-ambient-destination-policy.md).
  - **Not covered at 0.33.0, deliberately; CLOSED in 0.35.0** — markdown link / image destinations
    passed the scheme floor only, because `Markdown.toHtml` is pinned by a canonical cross-host
    corpus and threading a policy through it is a wire-adjacent change rather than a call-site
    adoption. Done as its own act below.
- **Markdown destinations are policy-checked as of 0.35.0 (Phase 1032) — BREAKING rendering
  behaviour under the default policy, additive on the public surface.** The gap 0.33.0 disclosed is
  closed, in the same three places it was opened.
  - **Additive:** `Markdown.toHtmlWithEgress : EgressPolicy -> string -> string` is new. The pure
    `Markdown.toHtml` is UNCHANGED and is now defined as the permissive case — `toHtmlWithEgress
    Sanitize.permissiveEgress`, byte-for-byte over the whole cross-host corpus. It survives rather
    than flipping its default because that corpus is a five-host byte-parity contract: changing the
    pure function would rewrite existing fixtures in every host in one act, and a mass churn is
    exactly where a real divergence hides. It is the right entry point for a HAND-AUTHORED body,
    where the author is the trust boundary.
  - **Behaviour-breaking, and the point:** the three renderer call sites — client, SSR, and the
    email projection — pass their context's `EgressPolicy`, whose default denies. A decoded markdown
    body whose link or image names an undeclared origin now renders
    `about:blank#fuaran-egress-refused` plus a `data-fuaran-egress-refused` marker naming the class
    and the host, where it previously rendered the destination. `mailto:` in a markdown body is
    refused by the same default, for the same reason, and a same-origin destination is untouched.
  - **The scheme floor's own answer is deliberately unchanged.** A URL the floor rejects
    (`javascript:`, an unknown scheme, a protocol-relative reference) still renders the bare
    `about:blank` with NO marker. That refusal is a different fact from a policy refusal and is
    pinned by the shared `sanitization/` corpus; re-spelling it inside a change about egress would
    churn that corpus where a genuine divergence could hide.
  - **The email projection drops the marker and keeps the refusal**, exactly as its `Link` and
    `Image` projections already did: `data-*` attributes do not survive the sanitisers most mail
    clients run, so a marker there is a signal that cannot be relied on to arrive.
  - **Cross-host:** this is a wire-adjacent forward-coupling event, not a local change. The refusal
    shape, the class assignment and the named corpus policies are specified language-neutrally in
    the wire format's §14.1, and the shared markdown corpus carries policied fixtures every
    conformant host renders. Full adoption guide:
    [`docs/migrations/1032-markdown-egress.md`](docs/migrations/1032-markdown-egress.md).

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
- `FragmentMemo.isCacheable` (the effect-class cache gate) + `FragmentMemo.BoundedLru` + `FragmentMemo.BoundedRefMemo` live in `Fuaran.UI` (Fable-clean, reusable). `FragmentKey.*` (the canonical-JSON-keyed content hash) lives here.
- **The structural key's composition changed in Phase 210 (`fuaran-fragment-apply:v1` → `:v2`).** The key is now composed from a separately-digested body (`FragmentKey.bodyDigest`, tagged `fuaran-fragment-body:v1`) plus the ref id and slot args (`FragmentKey.structuralOf`), so a probe no longer re-encodes and re-hashes the whole fragment body. `FragmentKey.structural` keeps its signature and its discrimination (the same `(name, body, refId, slot-args)` tuples key apart); what changes is the key VALUE. The key was never a wire artefact and no fixture pins it, so this is not a wire-format event — but a **portable-store snapshot** (`MemoCacheStore.Snapshot`, Phase 360) persisted by a pre-210 build keys its entries under the old composition, so it MISSES rather than mis-hits after the upgrade. A host that persists memo snapshots across versions should discard them on this bump; correctness is unaffected either way.

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

  **But the relay's own stability contract is the `relay@1.1` profile and its conformance corpus, not this package's semver.** Those are different axes and the profile is the binding one: adding a request type, a capability, an optional payload field, or a refusal class is a **minor** relay bump; removing or renaming any of them is a **major** one; and a change to the profile *id* breaks every peer regardless of what `Fuaran.UI.Renderer`'s version does. A host may advance its wire profile (`core@1.0`) without advancing its relay profile, and the reverse. See `DEVTOOLS_RELAY.md` §5.3 in the wire-format specification repository.

  Phase 739 additions to `DebugGlobal.ApplyOutcome` (`AppliedWithTree` / `DecodeFailedWith` / `RejectedWith`) are **additive cases**: an existing host's construction sites are unchanged, and a host that stays on `Applied` / `DecodeFailed` / `Rejected` keeps working — it merely forgoes the exact-revision guarantee and the typed refusal detail the new cases carry.

  **`relay@1.0` → `relay@1.1` (0.32.0) — `read.affordances`.** A new request type, a new capability named identically to it, and a new response payload: additive, so a minor relay bump by the rule above. Two things about it are worth stating because they are what make the bump safe rather than merely legal.

  First, **a `relay@1.0` client is still served, at `relay@1.0`.** §6.3 requires the profile named in `hello.ok` to be one the client listed in `accepts`, and the peer now picks the *highest* profile that is both acceptable to the client and speakable by this peer, instead of offering only its own. Answering only with its own would have refused every client whose `accepts` predates the newest minor — the entire population a backward-compatible bump exists to keep serving. Capabilities are advertised, and the per-request capability check is made, at the *negotiated* minor: a `relay@1.0` session is neither offered `read.affordances` nor served it, and gets the same `CAPABILITY_ABSENT` a genuine 1.0 peer would give, so the answer does not depend on which peer happened to receive the request. All 24 shipped `relay@1.0` corpus fixtures pass unchanged against the 1.1 peer.

  Second, **the entry point reports what a host DECLARED, never what the tree contains.** It is served from `Fuaran.UI.Renderer.Affordances` — a registration point plus a generic vocabulary, holding no declarations of its own — so a page with no registered provider answers an empty, well-formed enumeration rather than an error, and a module or field a provider does not publish is simply absent. That absence is the deny mechanism: there is no refusal class for a withheld module, because a refusal would disclose that something was withheld. `Affordances` is the seam and IS supported public surface; `RelaySurface`'s new `Affordances` field is not (it is part of the debug-only shape above, and `Relay.surfaceOf` fills it).

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

**Migration.** A `0.3.x` emission decoded for the length of a migration window: the decoder accepted
and ignored a legacy `position` / `newPosition`, applying the op as an append. That was a migration
mechanism for the hosts adopting independently, not a supported authoring form — the encoder never
wrote the field. **The window is now CLOSED — see 0.37.0 below.**

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

## Recorded breaking change — 0.15.0, Mount + custom-renderer isolation (fuaran#783)

**Breaking, on the two surfaces that sit outside the dispatch gate structurally** — so
[0.14.0](#recorded-breaking-change--0140-the-dispatch-gate-fails-closed-fuaran782)'s inversion did
not reach them. Both are described in the design docs as isolation boundaries; neither was one for a
**decoded** tree.

**1. A decoded `Mount` is clamped to `OutOnly`.** `ChannelDirection` is a *required* wire field, so a
hostile tree simply wrote `TwoWay`; `OutOnly` was only the default of the authoring smart
constructor, and no host-side clamp existed. `TwoWay` is now a host grant via the new
`GuestSeam.GrantTwoWay`, and the downgrade is recorded rather than silently applied. The clamp is at
the **renderer**, not the decoder — so canonical round-trip and the shared conformance corpus are
untouched, and the host's policy decides what is honoured rather than the codec deciding what is
representable.

**2. No seam means unprivileged.** With no `GuestSeam` installed the `Mount` arm handed the guest the
HOST's runtime, unwrapped. It now hands it `Runtime.UnprivilegedGuestRuntime`: every capability
refused and recorded, `CanDispatch` false, no custom renderers in any scope, and no nested guest
loading. The previous entry for `GuestSeam` in this document said in as many words that changing the
no-seam default would be breaking for every host that installs nothing. It is, and it is the right
break.

**3. The custom-renderer registry is scope-constrained.** It was one process-wide dictionary keyed on
`(moduleId, componentId)` taken straight off the wire, so any decoded tree could invoke any renderer
registered anywhere in the process, with attacker-chosen props — a renderer registered for a
privileged admin surface was reachable from a tree rendered on a public one. Both registries (client
`CustomRendererRegistry`, server `Registry.ServerCustomRendererRegistry`) now key on
`(scope, moduleId, componentId)`; `None` is the root scope where the unscoped `Register` / `register`
land, so an existing single-surface host is unaffected. **There is no cross-scope fallback**, which is
what makes it a boundary rather than a hint.

`IFuaranRuntime` gains `TryRenderCustomInScope` / `TryGetCustomRendererInScope` — the members the
renderer's `Custom` arm now calls. Per this document's pre-1.0 minor-add precedent, direct
implementers add them; note the **fail-closed** consequence, which is deliberate: an implementer that
has not added them loses custom rendering rather than silently keeping the unscoped behaviour.
`ServerRenderContext` gains a required `Scope` field.

**4. The hash bypass is closed.** `ContentHash` is drift detection between a registered renderer and a
replayed tree; it is not, and cannot be, authentication of the tree, because the tree supplies its own
hash record. Two bypasses followed from reading it as more: omitting the hash classified as
`NoTreeHash`, which shared a render branch with `Match` and rendered **silently**; and strictness was
read from the *tree's own* record, so an attacker who declared a hash chose `AdvisoryWarning` and got
warn-then-render. Strictness is now a host floor (`Fuaran.UI.Renderer.CustomHash`, in the
emission-agnostic core so both renderers share one verdict) that a tree may only **tighten**, and
under an enforcing floor an unverifiable hash is a refusal.

**The floor defaults to `AdvisoryWarning`** — today's behaviour — because a tree with no hash is the
common legitimate case and an enforcing default would refuse most existing `Custom` nodes on upgrade.
Enforcement is a host act; what 0.15.0 changes is that a host *can* enforce, and that a tree cannot
talk its way underneath the host's choice. That is a deliberate limit on how much this version fixes,
and it is stated rather than implied.

**Migration.** [`docs/migrations/783-mount-custom-renderer-isolation.md`](docs/migrations/783-mount-custom-renderer-isolation.md).

## Recorded breaking change — 0.14.0, the dispatch gate fails closed (fuaran#782)

**Breaking, and the direction is the point: a security default has to fail closed.** The published
claim is that any capability a generated UI reaches "is denied by default and permitted only through
an explicit allow-list". In every shipped runtime `CanDispatch` returned `true`. The seam existed,
was correct, and was wired to the wrong default — which is worse than an absent gate, because the
claim reads as satisfied.

**What changed.**

1. **The default is DENY** in `DiagnosticRuntime`, `MutableRuntime`, `BrowserRuntime`, the .NET
   layout-observer fallback runtime, `DriverServices.create` and `BoundedServices.create`.
2. **The descriptor set is CLOSED.** `ActionDescriptor` gains `Notify of channel`,
   `SetState of key`, `WriteToClipboard` and `CommitLocal of nodeId`, and the corresponding
   `runAction` arms now route through `applyDispatchGate`. Before this, a host with a perfect
   deny-all policy still could not refuse a decoded tree's `SetState` — which writes the
   process-global `StateStore` and, on the browser path, persists into a `localStorage` namespace
   shared with host-owned keys. The old doc-comment's reason ("they route through their own
   substrates") described a routing detail, not a reason they were unreachable.
3. **`Action.Navigate` is sanitised on the ACTION path**, in both the client renderer
   (`Render.treeNavigate`) and the two server-driven interpreters, not only where an `href`/`src` is
   rendered. The shipped browser runtime assigns `window.location.hash` and is incidentally safe,
   but `IFuaranRuntime.Navigate` is documented as the seam a host wires to its SPA router, and
   `location.href` / `router.push` turn a `javascript:` route into script execution and any absolute
   URL into an open redirect. The check sits on the **canonical decoded field**, so the wire's
   `route` / `href` / `url` / `to` aliases are covered by construction rather than by enumeration.
   A refused route emits nothing — not `about:blank`, which is a navigation the author did not ask
   for.
4. **A host-reserved State namespace** (`Fuaran.UI.Renderer.StateKeys.HostReservedPrefix = "host."`).
   Every tree-originated State write — `Action.SetState`, a covered control's write-back default, a
   `Call … into State` target, and the bounded server-driven interpreter's `SetState` — refuses a key
   under that prefix and records the refusal. This is deliberately NOT gate policy: it holds even
   when `CanDispatch` allows everything, because which of a host's own key names are sensitive is not
   something a shipped default can know.

**What this does NOT do, stated so nobody assumes otherwise.** Tree writes are not themselves
re-namespaced into a sandbox. The declarative write-back loop (a control writes `Binding.State k`,
every reader of `k` re-resolves) requires tree writes and tree reads to name the same key, and the
host merges its own `BindingSources.State` seed under those same names — so prefixing tree writes
would either break reactivity or need an un-prefixing projection at read time that reintroduces the
collision it removed. Reserving a namespace the tree cannot address closes the same class from the
other side. A host with a sensitive slot named `theme` rather than `host.theme` is still reachable;
renaming it is the migration.

**Migration.** [`docs/migrations/782-default-deny-dispatch-gate.md`](docs/migrations/782-default-deny-dispatch-gate.md)
— one page, copy-pasteable, with the "my actions stopped working" symptom first. In short: implement
a real `CanDispatch` allow-list, or name one of the `permissive` constructors. One grep for
`permissive` then finds every place in a codebase where the old behaviour is back, which is the whole
reason the opt-in is a name rather than a boolean argument.

**Version axis.** Pre-1.0, so a minor bump carries a breaking change. It is genuinely breaking on
behaviour rather than on signature: a host that compiled before compiles after, and then refuses the
actions it used to perform. That is the loudest safe way for this particular change to arrive — a
silent re-enablement would have wasted it.

## Recorded change — 0.13.0, wire resource limits (fuaran#781)

**Minor, not major — but it narrows what the decoder accepts, and that is stated rather than
buried.** New module `Fuaran.UI.WireLimits` (literals only): `MaxDepth = 24` (node nesting),
`MaxJsonDepth = 256` (syntactic JSON nesting), `MaxStringLength = 1048576`,
`MaxArrayLength = 100000`, `MaxNodes = 100000`. These are the F# host's expression of the normative
limits in [`WIRE_FORMAT.md`](../wire-format-fixtures/WIRE_FORMAT.md) §21 — protocol numbers, not
implementation details. Changing one is a wire-format change and moves across every host, not here
alone.

**Why the change exists.** The claim that decoding is total — "a malformed or hostile input yields a
structured, typed error, never an exception or a hang" — held on semantics and was **false on
shape**. Every walk in the stack (the hand-rolled parser, the structural decoder, the `JVal`
bridges, `PreEmitValidate`, the server-side renderer, the interaction-cost accounting) was plainly
recursive with no counter, so a few hundred kilobytes of `[[[[[…` produced a
`StackOverflowException` — which .NET cannot catch. Past that point no `DecodeError` of any kind
could be returned, so the guarantee did not degrade, it became unobservable.

**Additive public surface (each a *minor* event per the Semver note above — existing matches gain
`FS0025`):**

- `JsonDecode.DecodeErrorCode` gains **`LIMIT_EXCEEDED`**. Per §6 this is the seventh code. A limit
  breach is deliberately **not** `INVALID_JSON`: the input is well-formed and merely unbounded, and
  reporting it as a syntax error sends the author to repair the wrong thing.
- `PreEmitValidate.PreEmitDefect` gains **`MaxDepthExceeded of nodeId * limit`** → **FUARAN091
  (Error)**. Reported once per tree, at the first node past the limit.

**Behaviour narrowing (the part a consumer must actually read).** A document nesting nodes more than
24 deep, or carrying more than 100 000 nodes, or a string over 1 MiB, or an array over 100 000
elements, or JSON nesting over 256, was previously decoded and is now refused with
`LIMIT_EXCEEDED`. `decodeOp` additionally refuses a `TreeOp.Batch` nested more than 24 deep —
a separate axis from node nesting, held to the same figure. (That walk was measured to kill the
process at 100 levels with every other bound already in place, on a 2.6 KB payload; the syntactic
bound was not cover for it.) This is on the same footing as §19's renderer URL-scheme floor: a narrowing whose
alternative is not "it worked" but "the process died". For scale, the deepest tree in the shared
corpus is 3 levels and a deliberately deep application tree reaches about 16, so no corpus fixture
and no realistic tree is affected — the whole existing suite is green unchanged.

**Server-side renderer.** `Render.renderStatic` / `renderWith` / `renderToElement` have total
signatures and so cannot return a refusal. A subtree past `MaxDepth` is replaced by a visible,
machine-readable marker element (`data-fuaran-depth-exceeded`) rather than recursed into. This also
closes a latent non-termination: a self-referential `FragmentDecl`/`FragmentRef` pair previously
recursed until the process died.

**`BoundedDriver` budget ordering.** `init` now prices the tree *before* resolving it, with the cost
walk iterative and stopping at `InteractionBudget.MaxNodes` rather than walking the whole tree. The
observable contract is unchanged — `init` stays total and `step` still rejects with
`BudgetExceeded` — only the work is.

**Cross-host status.** The limits are spec-level; the TypeScript, Python, Go and Rust hosts have not
adopted them, and `LIMIT_EXCEEDED` reject fixtures are deliberately **not** in the shared corpus
until they do (§21.5) — a fixture landing ahead of the hosts turns their builds red for a rule none
of them has adopted.

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

## Recorded change — `Fuaran.UI.Validator` manifest generation (fuaran#377)

**Additive; no behavioural change to any existing check.** The validator manifest is now DERIVED
from the consumer app's own source rather than hand-written, so the artefact grounding every
schema-coupled check is itself grounded. New public surface:

- `Fuaran.UI.Validator.ManifestEmitter` — `derive` / `toManifest` / `mergeOverrides`-driven `run`,
  `renderJson`, `diff`, `write`, plus the `MsgCase` / `Derivation` / `Provenance` / `Drift` /
  `EmitOptions` / `EmitOutcome` types.
- `Manifest` gains `manifestFileName`, `overridesFileName`, `discoverOverrides`, `mergeOverrides`.
- `AstWalker` gains `parseTree` — the parse plumbing the declaration walkers share.
- The CLI gains an `emit-manifest <project.fsproj> [--out] [--overrides] [--check]` subcommand.
  The existing positional invocation is untouched.

**Not a wire-format change.** The emitted manifest is the same v1 shape `Manifest.parse` already
consumes, plus an additive `$generated` provenance key the parser ignores — so a generated manifest
and a hand-written one are read identically, and the validator itself is unmodified. The
hand-written path survives as the override tier
(`fuaran-validator.manifest.overrides.json`), merged over the derived base at generation time.
Contract + migration note: [`docs/VALIDATOR-MANIFEST.md`](docs/VALIDATOR-MANIFEST.md).

## Recorded change — op-stream verification on the read paths (fuaran#793)

**Minor.** New public surface plus a behavioural tightening on paths that previously performed no
check at all. Nothing about the chain FORMAT changes — no new pre-image, no re-hash, no corpus move;
a stream written before this change verifies unaltered.

### 1. `Fuaran.UI.OpStream.Abstractions` gains a segment verifier and a load-mode — additive

- `Verify.segmentFrom expectedPreviousHash expectedFirstSequence records` — verify a contiguous
  segment against an anchor the caller already trusts. `Verify.chain` is now `segmentFrom` at
  `(genesisPreviousHash, 1)`, so its behaviour and error shapes are unchanged.
- `Verify.segment records` — the anchor-relative form for a reader holding no external anchor
  (a `Replay(from, to)` slice, a compacted stream). A segment starting at sequence 1 must still carry
  the genesis anchor, so a whole-stream read is verified exactly as strictly as before.
- `Verify.loaded` / `Verify.describe` — the sink-facing entry point and its message renderer.
- `LoadVerification` (`Full` / `Tail n` / `Off`) — how much of a loaded segment a read path
  re-verifies.

**Why `segmentFrom` had to exist:** `Verify.chain` hardcodes a genesis start, so it reports every
legitimate mid-stream read as `OutOfOrder` and could not be wired into any sink. One consumer had
already hit that and written its own walker (`SessionStore.verifyTail`), which is now retired onto
the shared one.

### 2. `ReplayError` gains `ChainBroken` — a new DU case, still minor

Same shape as the [`DagReplayError` precedent](#2-dagreplayerror-gains-snapshothashmismatch--a-new-du-case-still-minor):
a consumer with an exhaustive `match` gets `FS0025`, which **is** a build break under
`TreatWarningsAsErrors`, and the [Semver](#semver) section classifies it minor regardless. Add the arm.

### 3. `DagVerify` gains `record` and `recordsResolving` — additive

`records` is retained and is now `recordsResolving` closed over the input set. The parameterised form
exists because parent linkage must be resolved against the WHOLE STORE, not the stream being read:
the guest-fork contract anchors a guest branch's genesis on the `Mount` op in the host stream, so a
stream-scoped record set is not a closed parent universe.

### 4. Read paths now verify — a behavioural tightening

`Replay.applyTo`, both linear sinks' `Replay`, and both DAG sinks' `Records` / `TryGet` verify what
they return. **A store that was already corrupt will now be refused where it previously replayed
silently** — that is the point, and it is the only way a consumer notices this change.

Three consumer-visible consequences:

- `Replay.applyTo` **refuses the whole stream** on a break rather than folding the clean prefix, and
  materialises its `records` argument (a lazy sequence is fully enumerated). `Replay.applyToUnverified`
  is the old body under a deliberately longer name, for a caller that verified upstream or is
  replaying synthetic records.
- The sinks have no error channel, so they refuse with an `InvalidOperationException` naming the
  stream and the offending record — the idiom they already use for a duplicate sequence.
- Verification is roughly linear at ~5 µs/record; it about doubles a 10,000-record Sqlite read and is
  unmeasurable on a single-record one. Opt down with `LoadVerification` at construction
  (`SqliteSink.createWith`, `InMemorySink.createWith`, `SqliteDagSink.createWith`,
  `InMemoryDagSink.createWith`). Measurements and the reasoning are in
  [`CRYPTO.md`](CRYPTO.md#verification-on-the-read-paths).

**Not a security-property change.** The chain stays UNKEYED: this makes the corruption detection it
genuinely provides actually happen, and adds no tamper evidence. See `CRYPTO.md`.

## Recorded change — 0.18.0, declarative sort intent on `staticRows` (fuaran#801)

**Additive wire vocabulary, and additive in the strict sense: no existing emission changes bytes.**
A `DataGrid`'s `staticRows` payload gains two OPTIONAL fields:

- `sortable: bool` — this table invites interactive column sorting.
- `defaultSort: { "column": <header index>, "direction": "asc" | "desc" }` — the table's initial
  order.

Both omitted ⇒ exactly the 0.17.0 wire. The full `--emit-corpus` regeneration moved no pre-existing
fixture payload by a single byte; it added one node fixture, two reject fixtures, and the
corresponding `manifest.json` / `schema.json` / `idl.json` entries. `nodes/table-1.json` is the
anchor for that claim and is deliberately left declaring nothing.

**Why it exists, in one line:** an emitter asked for "a sortable table" had no way to say so —
sortability was invisible to the wire and therefore unteachable to a model, and the reference table
enhancement shipped at [fuaran#800] is a host-wide on/off with no per-table intent to read.

**Intent, not behaviour.** `sortable` declares what the table INVITES, not what a host guarantees. A
host with no sorting affordance renders the authored order and is fully conformant; a grid-backed
host may map the declaration onto its own sorting. Nothing in the wire promises an interaction.

**`defaultSort` is configuration, not a data transform.** It is deliberately distinct from the
transform pipeline's `sort`, which re-orders the DATA. `defaultSort` states an initial presentation
order over rows the emitter already wrote, and the authored order stays recoverable — the reference
enhancement seats a declared sort inside its existing ascending → descending → authored cycle rather
than beside it, so the emitter's order is always reachable.

**Additive public surface (each a *minor* event per the Semver note above).**

- Generated types: `Fuaran.UI.Generated.DefaultSort` (record) and `SortDirection` (`Asc` / `Desc`),
  re-exposed as `Fuaran.UI.Types.DefaultSort` / `SortDirection`. Modelled in the IDL, so they are
  generator output, never hand-written.
- `Generated.StaticRows` gains `DefaultSort: DefaultSort option` and `Sortable: bool option`. **A
  record field addition breaks construction sites** — every `{ Headers = …; Rows = … }` literal needs
  the two new fields. This is compiler-forced and the count is small and knowable (four sites in this
  repo). Pattern matches over the record are unaffected.
- `TableSpec<'Msg>` gains matching `Sortable` / `DefaultSort` slots, and `Fuaran.sortableTable` is
  the shorthand constructor. `Defaults.table` supplies `None` for both.
- `JsonDecode.DecodeErrorCode` is **unchanged** — no seventh-plus code was needed.

**Wire contract.** `"staticRows": { "defaultSort"?: {"column": <int ≥ 0>, "direction": "asc"|"desc"},
"headers": [...], "rows": [...], "sortable"?: <bool> }`. Two decode rejections: a `direction` outside
the closed pair is `UNKNOWN_DU_CASE` at `$…staticRows.defaultSort.direction` naming both legal
values, and a negative `column` is `WRONG_TYPE` at `$…staticRows.defaultSort.column`. The published
schema says the same thing (`minimum: 0` and an `enum`), so both fixtures are schema-invalid as well
as decoder-rejected. A column index PAST the end of `headers` is deliberately NOT a decode error — it
is a cross-field relation the codec does not judge, and a host that cannot resolve the column renders
the authored order. Corpus: `nodes/table-sortable-1.json`,
`reject/reject-unknown-static-sort-direction.json`, `reject/reject-wrongtype-static-sort-column.json`.

**Renderer.** Both the client renderer and the SSR renderer emit `data-fuaran-sortable` and, when a
default sort is declared, `data-fuaran-sort-column` / `data-fuaran-sort-direction` on the
`<table class="fuaran-table">`. **Emitted only when the field is present**, so an undeclared table's
markup — and the server-rendered bytes a deterministic-render gate hashes — is unchanged. The
reference enhancement (`content/fuaran-reference-tables.js`) reads those attributes:
`data-fuaran-sortable="false"` exempts a table entirely, ahead of any DOM mutation, so an exempted
table advertises nothing.

**Host adoption.** The F# reference, the shared corpus and the TypeScript tier land together; the
Python, Go and Rust codec legs follow by fixture (the 725 / 730–733 precedent).

**Authoring veneers not extended.** The C# fluent factory and the VB XML mapping still author a table
without the sort slots. `WIRE_FORMAT.md` §11 step 6 is written for `NodeKind` additions and its gates
pin KINDS — `Fuaran.UI.CSharp.Conformance.Tests/Coverage.cs` reflects `NodeKind` cases and the VB
analyzer pins `Vocabulary.Kinds` — so a payload-FIELD addition binds neither veneer and neither gate
fails. Exposing the slots there is a follow-up, recorded here so it reads as a decision rather than
an oversight.

## Recorded change — 0.23.0, the reactive-derivation first cut (fuaran#818)

**One rule, operator-approved 2026-08-13 (O1+O2+O3 together; the `valueFrom` field name; the
grid-sort header affordance pulled INTO the first cut): any read slot that today takes a literal may
take a Binding; the runtime evaluates bindings with subscription semantics; the Transform verb set is
the only computation vocabulary.** The wire is additive; the host surface takes deliberate
source-breaking changes classified pre-1.0 minor per the [Semver](#semver) note and the 0.5.0
precedent.

**O1 — live Transform sources.** A `Transform` whose `source` is binding-shaped
(`{"$type":"State"|"Selection"|"Query",…}`) is now PRESERVED rather than snapshotted: the 0.20.0
(fuaran#815) leniency decoded that shape as an initial-value snapshot; 0.23.0 upgrades its
*semantics* without re-encoding anything.

- Host type: `Binding.Transform`'s source slot widens from `Fuaran.Core.DataSource` to the host DU
  `TransformSource` — `Data of Fuaran.Core.DataSource` (the pre-818 shape) `| Live of binding:
  Binding<JVal> * initial: Fuaran.Core.DataSource`. `initial` is the decode-time snapshot derived
  from the binding's carried default data (never encoded — the binding IS the wire form).
  **Source-breaking for constructors and full-arity matches** on `Binding.Transform`; wildcard and
  pass-through matches compile unchanged. `Fuaran.binding.transform`/`transformWith` keep their
  `DataSource` signatures (they wrap); `binding.transformLive` is the new live-source constructor.
- Wire: **one dialect** — canonical re-encode reproduces a binding-shaped source byte-for-byte
  (pinned by the upgraded `lenient-transform-source-state-rows` corpus pair, whose expected file now
  equals its input). A canonical columnar / `ref` source is byte-identical to 0.22.0.
- Runtime: the resolver (`BindingResolver.evalTransformFrame`) resolves a `Live` source against the
  reactive stores and evaluates the pipeline over the CURRENT data (row-major store values transpose
  through the same 815 normalisation, now shared at `Fuaran.UI.HostPrelude.TransformLive`); an
  unwritten store falls back to `initial`, which is what keeps SSR byte-identical to the 815
  snapshot (pinned in `Renderer.Server.Tests`). The client reactive walk (`keysOfBinding`)
  contributes the live source's channel keys, so a `SetState` on the source key re-evaluates every
  reader. A State-shaped source still requires carried data (the 815 didactic for an empty wrapper is
  unchanged); Selection/Query sources start from the empty table. Non-tabular live values error
  loudly, never silently.
- New public resolver surface: `BindingResolver.jvalOfResolved` (store-value → `JVal` lift, per-host
  legs), `resolveJVal`, `readSortDescriptor`, `sortRowsByDescriptor`.

**O2 — `Switch.on`.** Already shipped as fuaran#768 (0.16.x era): `on` accepts any Binding with
`stateKey` as the compact State-form spelling. Re-pinned here as part of the family; no change.

**O3 — `SetState.valueFrom`.** `Action.SetState` becomes
`SetState of key: string * value: JVal option * valueFrom: Binding<JVal> option` — `valueFrom` is a
SIBLING of `value`, never a reinterpretation (a binding-shaped object in the `value` slot remains a
legitimate literal). Decode enforces value XOR valueFrom: both-present is a `WRONG_TYPE` didactic
naming both fields (corpus `reject-setstate-value-and-valuefrom`; the schema's `oneOf` rejects it
too), neither-present a `MISSING_FIELD` naming the alternative. `valueFrom` evaluates AT DISPATCH
TIME inside the existing gate + host-reserved-key guard, on both the client runtime (`runAction`)
and the bounded server driver (`BoundedActions`); an unresolved/unliftable source performs NO write
and is warned/diagnosed. **Source-breaking for `Action.SetState` constructors/full-arity matches**;
`Action.setState` keeps its two-argument signature and `Action.setStateFrom` is the derived-write
constructor.

**Grid-sort header affordance — `DataGridSpec.sortStateKey: string option`** (encode-omitted when
absent; record-field addition breaks construction literals, compiler-forced). Semantics, for a
DATA-BOUND grid: the runtime renders column headers as sortable affordances reusing the Phase-801
static-table presentation vocabulary (`data-sortable` + live `aria-sort`; the reference CSS
selectors now cover `.fuaran-grid-header` alongside `.fuaran-table-header`); clicking header N
dispatches the equivalent of `SetState(sortStateKey, {"column": N, "direction": "asc"|"desc"})`
(asc ⇄ desc toggle, routed through the ordinary tree-write path so gate/guard/scope apply); the grid
sorts its RESOLVED rows by the state-carried descriptor before rendering (runtime-side sort — the
author wires no Transform; empty cells last in both directions; stable ties; a malformed descriptor
or out-of-range column leaves the authored order standing). **Sorting keys off the addressed
column's `field`** — a field-less closure column is not sortable and renders without the affordance.
SSR: the data-bound grid keeps its hydration placeholder (no state ⇒ natural source order); a
`staticRows` grid's Phase-801 `sortable`/`defaultSort` path is untouched. The grid subscribes its
`sortStateKey` on the State channel, so descriptor writes re-render it.

**Corpus.** New node fixtures `badge-transform-live`, `button-setstate-valuefrom`,
`grid-sort-state-key`; new reject fixture `reject-setstate-value-and-valuefrom`; the
`lenient-transform-source-state-rows` pair upgraded in place; `schema.json` regenerated (SetState
`oneOf`, `sortStateKey`, the widened Transform-source comment). `Switch.on` was already pinned by
`switch-on-selection`. No pre-existing fixture moved a byte.

**Host adoption.** The F# reference and the shared corpus land together; the TS/Python/Go/Rust legs
follow by fixture (the 801 precedent). The C#/VB authoring veneers do not yet expose the new slots —
same posture as 0.18.0, recorded as a decision.

## Recorded change — 0.24.0, implied-node-close decode recovery (fuaran#850)

**`Fuaran.UI.Ops.JsonDecode` recovers the one malformed-emission class the wire grammar cannot
teach away**: a node wrapper's closing brace dropped at the end of a `children[]` / `cases[]`
element (or the root node left open at end of input), typically after a run of ≥ 2 closing braces.
Measured on stored model emissions (2026-08-15): auto-close at EOF — the obvious fix — repairs
none of the mid-document cells (pinned as a test so the wrong fix cannot return); auto-close on an
**ancestor-legal token** recovers 36/36, with 34/36 decoding clean (the two residuals are
unrelated `MISSING_FIELD` defects). Full contract:
[`docs/migrations/850-implied-node-close-recovery.md`](docs/migrations/850-implied-node-close-recovery.md).

- **Additive decode behaviour — pre-1.0 minor** (the 0.20.0 leniency precedent). Every document
  that parsed before decodes byte-identically (the recovery is reached only after the ordinary
  parse fails); every document that failed with a code other than the recovered class's
  `INVALID_JSON` profile keeps its exact error. The newly-accepted inputs were rejections before,
  so no existing consumer behaviour changes.
- **Bounded, profile-gated, fail-closed.** Insert-only owed closers; mid-document closes fire only
  into an array keyed `children` / `cases`; ambiguous nesting, truncation fingerprints,
  over-closed documents, and `LIMIT_EXCEEDED` all keep the original error. `decodeOp` is
  untouched (strict parse — the class is node emissions).
- **New public surface: `JsonDecode.Reliance`** — the recovery accounting read side (`count` /
  `snapshot` / `reset`) with the literal counter id `Reliance.ImpliedNodeClose` =
  `"implied-node-close"`. This is error RECOVERY, not §16 shorthand normalisation: every recovery
  is counted and surfaced the way §16 leniencies are measured, so the class stays measurable
  while ceasing to be a loss. The counter id string is a stable identifier (same footing as a
  `DecodeError` code).
- **Corpus / spec / host parity — staged, not shipped here.** The shared-corpus fixture family,
  the `WIRE_FORMAT.md` §16-adjacent optional-host-behaviour note, and the sibling-host ports
  follow deliberately (the 0.20.0 staging pattern); until then the in-repo
  `recovery-fixtures/` suite (the 36 stored emissions) is the acceptance record.

## Recorded breaking change — 0.25.0, the bounded program path leaves `Fuaran.UI.ServerDriven` (fuaran#756)

`Fuaran.UI.ServerDriven` **no longer ships the bounded (no-`'Msg`) program path**. Three surfaces are
removed from the package:

- `Fuaran.UI.ServerDriven.BoundedActions` — the bounded-`Action` interpreter, with `BoundedStore`,
  `BoundedOutcome` and `BoundedDiagnostic`.
- `Fuaran.UI.ServerDriven.BoundedDriver` — the no-`'Msg` driver, with `BoundedServices`,
  `BoundedSession`, `BoundedReject`, `BoundedStepOutput` and `InteractionBudget`.
- `BoundedDriver.resolveTree` — the binding re-resolution pass.

They **moved whole** to `Fuaran.Program.Bounded`, the program domain's package (Apache-2.0), where
`resolveTree` now lives at `Fuaran.Program.Bounded.Resolve.resolveTree` and the rest keep their
names. Behaviour is unchanged: this is a relocation, not a rewrite, and the moved invariant suite
(no closure is ever invoked; `SetState` is the only mutation) moved with it.

**Why the code moved rather than the package gaining a dependency.** The interpreter is shared by two
placements of the same loop — a server session and a browser client — and "one algebra, two
placements" only holds if there is exactly one interpreter. Keeping the driver here and referencing
the domain package would have made this repo depend on a package that itself depends on `Fuaran.UI`,
putting two builds of the same types in one compilation and turning every `Fuaran.UI` type change into
a two-repo round trip. The dependency therefore runs one way only — the program domain consumes
`Fuaran.UI.*`, and no `Fuaran.UI.*` package references `Fuaran.Program.*`. The reasoning is recorded
as D5 in that repo's `DECISIONS.md`.

**Migration.** A consumer of the bounded path adds a `Fuaran.Program.Bounded` package reference and
changes `open Fuaran.UI.ServerDriven.BoundedDriver` to `open Fuaran.Program.Bounded.BoundedDriver`
(plus `open Fuaran.Program.Bounded` for the interpreter types). `resolveTree` call sites become
`Resolve.resolveTree`. No other edit is required — the types and their members are unchanged.

**Unaffected.** The server-driven transport core stays exactly where it is: `DomPatch`,
`ClientEffect`, `Lowering`, `Validation`, `Inbound`, the hand-authored `Driver`, `FormValidation`,
`FormBuffer`, `Navigation`, `InteractionTelemetry`, `SessionStore`, `Channel` and `FrameWire`. A
consumer that never touched the bounded path needs no change at all.

A removal is a major-versus-minor question the pre-1.0 caveat settles: `Fuaran.UI.ServerDriven` is
pre-1.0 and not in the Scope table above, and this ships as a minor bump on the 0.5.0 precedent.

## Recorded change — 0.26.0, uniqueness-gated over-close recovery (fuaran#855)

**`Fuaran.UI.Ops.JsonDecode` gains the MIRROR of the 0.24.0 recovery, with the opposite default.**
The class is the same emission boundary with the sign reversed: a closer the emission does not owe
(`…}}}` where `}}` was owed), closing one level past the node. 0.24.0 inserts what the grammar
FORCES; this deletes what the grammar merely PERMITS, and the two are not symmetric problems — an
owed closer has exactly one legal home, a surplus closer has as many as there are enclosing levels,
and every choice re-assigns the fields that follow it to a different owner. Measured on the stored
instances: after decoding every candidate through this decoder, six of the first sixteen still admit
two to five repairs that EACH decode clean, differing only in silent field ownership. So the gate is
uniqueness and the default is refusal. Full contract:
[`docs/migrations/855-uniqueness-gated-overclose-recovery.md`](docs/migrations/855-uniqueness-gated-overclose-recovery.md).

- **Additive decode behaviour — pre-1.0 minor** (the same 0.20.0 precedent). The gate is reached
  only after the ordinary parse fails AND the 0.24.0 recovery declines, so every document that
  decoded before decodes byte-identically, and every failure outside the over-closed profile keeps
  its exact error. The newly-accepted inputs were rejections before.
- **Accept iff EXACTLY ONE candidate decodes clean.** Candidates are the document with one or two
  structural closers deleted, enumerated exhaustively within stated bounds (surplus ≤ 2, ≤ 512
  closer positions, ≤ 8192 deletion sets, ≤ 32 distinct parseable candidates) and de-duplicated by
  parsed value. Zero clean, two or more, or an enumeration past the bounds all return the ORIGINAL
  `INVALID_JSON` unchanged. The failure-offset selection rule informs candidate ordering only and
  cannot change the verdict — uniqueness is a property of the whole enumeration. `decodeOp` is
  untouched (strict parse).
- **New public surface: two counter ids on the existing `JsonDecode.Reliance`** —
  `Reliance.OverCloseUnique` = `"over-close-unique"` (an acceptance) and `Reliance.OverCloseRefused`
  = `"over-close-refused"` (a refusal). **Deliberately distinct**, and the refusal is counted for
  the same reason the recovery is: a class that is silently recovered stops generating demand
  signal, and one that is silently refused stops generating it too. Both strings are stable
  identifiers (same footing as a `DecodeError` code).
- **The wrong fix is pinned, not merely avoided.** The leftmost-legal deletion — the obvious
  implementation — decodes perfectly clean on the worst measured cell while burying five trailing
  fields inside a `Static` binding. It is committed as a negative fixture, so a future change that
  reaches for leftmost-first fails the suite.
- **Corpus / spec / host parity — staged, not shipped here**, on the 0.24.0 pattern. The in-repo
  `overclose-fixtures/` labelled set (28 stored emissions, 14 with their committed intended repair)
  is the acceptance record until then. The class must NOT be added to the `lenient-accept` corpus
  family, which is for loss-free spelling normalisation only.

## Recorded breaking change — 0.29.0, the user-action record (fuaran#889)

**What it is.** The op stream records what the AI AUTHORED; nothing recorded what a USER did.
`ActionInvocation` closes that: one record per dispatched user gesture, carrying the `Action`
constructor, the `(NodeId, DOM event name)` pair, the outcome (dispatched / denied / failed), the
affordance's provenance, the dispatch path and the Phase 330 interaction id, with a sink seam and a
durable SQLite log.

**Why it is not on `IFuaranTelemetrySink`.** Phase 866's charter settles that a trigger may point at
the `Action` vocabulary and nothing else, so a user action is never a `TreeOp` and cannot ride
`IOpStreamSink`'s `OpRecord`. It gets `IActionInvocationSink` — a NEW interface, so no existing
implementer is touched. Adding a seventh member to `IFuaranTelemetrySink` would have cost every
direct implementer a stub (Phase 330 measured 8 in this repo and 11 downstream) to reach a tier that
ships no durable implementation at all.

**Additive (no consumer edit):** the `Fuaran.UI.Ops.ActionInvocation` module (the record,
`ActionCaptureMode` / `ActionOutcome` / `DispatchPath` / `AffordanceProvenance`,
`IActionInvocationSink`, `ActionInvocationSink.noop` / `Collector`); `ActionInvocationSqliteSink` in
`Fuaran.UI.OpStream.Sqlite` (its own `action_invocation` table, NOT `op_stream`); the render entry
`Render.renderWithSourcesSinkContextAndActionSink`; and the `Result`-returning gate variants
`Render.applyDispatchGateOutcome` / `treeStateWriteOutcome` / `treeNavigateOutcome` (the existing
`unit`-returning three now delegate to them, behaviour and diagnostics byte-identical).

**Breaking — two required record fields, the 0.5.0 shape exactly:**

```fsharp
// 0.28.0
{ Sources = …; SessionContext = Map.empty }

// 0.29.0 — every RenderContext literal names both new fields
{ Sources = …; SessionContext = Map.empty; ActionSink = None; CurrentNodeId = None }
```

`RenderContext.ActionSink : IActionInvocationSink option` is the recording seam; `None` is the
shipped default at every convenience entry and records nothing. `RenderContext.CurrentNodeId :
string option` is set once per node by `render` (only when a sink is wired, so an unrecorded render
pays no per-node record copy) — there is no other route to the node a handler belongs to, because
`runAction` is handed the resolved `Action` and nothing else.

`DriverServices` gains a required `ActionRecording : ActionRecordingServices option`. `None` — what
`DriverServices.create` / `createPermissive` supply — records nothing; a host builds its services
with `{ create render with … }` and is unaffected. Its `CorrelationContext` is a THUNK read per
step, not a value: `DriverServices` is built once per connection while the interaction id changes
every turn, so a captured map would stamp the first turn's id onto every later one.

**Behavioural change, deliberate.** `Validation.describeAction` now delegates to
`ActionInvocation.describe`, and `Navigate` prints its PATH rather than the whole route. That text
reaches a host's logger through `RejectReason.describe`, and a route's query string carries user
data — the pre-889 spelling leaked precisely what a log-safe describer exists to withhold.

**Privacy posture, and it inverts what a reader might assume.** The default capture mode is
`Redacted`: the action CONSTRUCTOR and the author-declared name that identifies it, never a payload
VALUE. `PayloadBearing` is a per-host opt-in declared on the SINK, so wiring a destination and
opting into payload values are two separate acts and neither is reached by omission. **The end user
is not the opt-in party** — this is host-side instrumentation, and obtaining a user's consent where
the host is user-facing is the host's obligation, which the redaction default does not discharge.
Retention is likewise the host's: an append-only sink is the retention boundary, and a policy baked
into a wire record is one every host inherits and none can change.

## Recorded change — 0.35.0, the WebSocket transport reaches the SSE transport's security posture (Phase 787)

**The behavioural half is a break for existing WebSocket hosts, and it is the point of the change.**
`GET /live/ws` previously accepted any upgrade: no principal was resolved, no token was checked, and
the fragment accumulator had no cap. It now refuses — **401, before the socket is accepted** — unless
the request carries a connection token that verifies against the resolved principal. A client that
simply opened the socket must now mint first.

**Migration (client).** Fetch the token, then connect:

```js
await fetch("/live/ws-token", { credentials: "same-origin" })
const ws = new WebSocket(`wss://${location.host}/live/ws`)
```

`mapFuaranLiveWebSocket` maps the mint endpoint itself, so no host wiring changes. The split exists
because a WebSocket upgrade is one request that both opens and authorises the session: there is no
second request to gate the way the SSE backend gates its `POST`, so the token has to exist before the
handshake or there is nothing to check.

Refusing **pre-accept** is load-bearing rather than stylistic. Once `AcceptWebSocketAsync` has run the
response is committed as a 101 and there is no status code left to send, so a post-accept check could
only close an already-established socket.

**Additive record fields, on the established pre-1.0 minor-add precedent.**

- `LiveWsConfig` gains `TokenPath`, `CookieName`, `Secret`, `ResolvePrincipal` and `MaxMessageBytes`.
  Its `Path` and `MakeSession` are unchanged, and `defaultWsConfig` fills all five — a host that
  builds its config through that function needs no edit.
- `LiveAppConfig` gains `MaxBodyBytes`, defaulting to the same shared constant. Phase 211 introduced
  the SSE cap as a literal inside the handler; a budget written inside a handler cannot be compared
  with the other transport's, so parity between them was unassertable. A host constructing either
  record with a bare literal recompiles; `{ defaultConfig … with … }` is unaffected.

**`ConnToken` moved to the transport-agnostic core** — `Fuaran.UI.ServerDriven.ConnToken`, so both
transports gate on ONE implementation rather than a copy each. `Fuaran.UI.ServerDriven.AspNetCore.ConnToken`
remains as a forwarding module, so a host that referenced the old path keeps compiling; new code
should call the core module. The module is server-only (`HMACSHA256` has no Fable mapping) and sits
under `#if !FABLE_COMPILER`, so the client shim's transpile passes over it. The new
`Fuaran.UI.ServerDriven.LiveLimits` holds the shared inbound budget and is Fable-clean.

**Why a shared implementation rather than a second correct copy.** A per-backend copy is how the two
postures diverged in the first place: Phase 211 hardened SSE and the WebSocket backend received none
of it, while its own config comment claimed the parity it lacked. The two packages still reference
neither each other nor anything new — what they share, they share through the core — and a
transport-parity test now compares the two configs against each other, so a third transport that
skips the posture fails a test rather than shipping quietly.

**Also in this change, and unrelated to the transports.** `RejectReason.PayloadOutOfBounds` no longer
echoes the client's submitted value into its detail string. The three bounds checks in `Validation.fs`
name the bound that was missed and withhold the value that missed it, so `RejectReason.describe`
keeps its own doc comment's promise by construction. The reject reason's SHAPE is unchanged — the
detail text is not part of the contract — but a host asserting on that text in a test will see it
change. The census entry is `docs/ACTION-LOG-PRIVACY.md`.


---

## Recorded change — 0.36.0, the declared field rule (fuaran#864)

**Additive wire vocabulary, and additive in the strict sense: no existing emission changes bytes.**
`FormField` gains one OPTIONAL slot, `rule`, carrying the constraint the field declares over its
value. Omitted ⇒ exactly the 0.35.0 wire. The full `--emit-corpus` regeneration moved no pre-existing
fixture payload by a single byte; it added one node fixture, three reject fixtures, and the
corresponding `manifest.json` / `schema.json` / `idl.json` entries. `nodes/form-declarative-minimal.json`
is the anchor for that claim and is deliberately left declaring nothing.

**Why it exists, in one line:** `required` was the entire constraint vocabulary, so every constraint
that is not "this field must be filled in" — a format, a pattern, a length, a comparison against a
sibling field — had exactly one place to go, the help text, and that is precisely where every observed
emission put it.

**A rule names an ACCEPTED SET; `FormFieldKind` names a CONTROL.** That one sentence is why this is a
record field and not a new `FormFieldKind` case, and the reasoning is in the vocabulary charter
([`docs/VOCABULARY.md`](docs/VOCABULARY.md) §2.1 + the form-constraint cluster). A `FormattedText`
case beside `Text` would manufacture one more instance of valid-but-wrong-kind selection — the worst
kind, because `Text` for an email field is not *wrong*, only less specific, so nothing can call it.
A field adds no choice to get wrong, and the confusion delta against the existing nine
`FormFieldKind` cases is structurally zero.

**The rule slot carries no numeric or temporal bound, and that is a rule rather than an omission.**
`RangedNumber` and `Date`/`DateRange` already carry `min`/`max`; a rule never duplicates a bound its
control already holds, because two sources for one bound are free to disagree. `compare` is not a
duplicate of them precisely because its operand is a `Binding` where theirs is a literal — which is
also the whole cross-field mechanism, since a form field's value already lives in State under its own
id and so `{"$type":"State","key":"<sibling id>"}` reads it with no addressing syntax of its own.

**Additive public surface (each a *minor* event per the Semver note above).**

- Generated types: `Fuaran.UI.Generated.FieldRule` / `CompareRule` (records) and `TextFormat`
  (`Email`/`Url`/`Tel`) / `CompareOp` (`Eq`/`Neq`/`Lt`/`Lte`/`Gt`/`Gte`), re-exposed as the matching
  `Fuaran.UI.Types.*`. Modelled in the IDL, so they are generator output, never hand-written.
- `Generated.FormField` gains `Rule: FieldRule option`. **A record field addition breaks construction
  sites** — every `{ Id = …; Kind = …; Label = …; Required = …; Help = … }` literal needs the new
  field. Compiler-forced, and the count is knowable (thirty-eight sites in this repo, all in tests,
  samples and the C# veneer). Pattern matches over the record are unaffected. `Defaults.formField`
  supplies `None`.
- `JsonDecode.DecodeErrorCode` is **unchanged** — no new code was needed; all three refusals are
  `WRONG_TYPE`.
- `PreEmitValidate.PreEmitDefect` gains three cases and one reason DU (`RuleSlot`). `describe` is
  exhaustive, so the codes cannot ship without their messages.

**Wire contract.** `"rule"?: {"compare"?: {"against": <Binding>, "op": "eq"|"neq"|"lt"|"lte"|"gt"|"gte"},
"format"?: "email"|"url"|"tel", "maxLength"?: <int>, "message"?: <TextSource>, "minLength"?: <int>,
"pattern"?: <string>}`. Three decode rejections, all `WRONG_TYPE`: a `rule` with every constraint slot
absent (a rule that constrains nothing is a defect, not a no-op, and a `message` alone is the help-text
failure wearing the new vocabulary's clothes); a `minLength` above its `maxLength` (the `DateRange`
ordered-pair rule on a length pair); and `validation` / `constraints` / `validate` on a `FormField`,
refused by name and pointed at `rule` (the near-miss narrowing of rule 2). The published schema states
the first and the third — an `anyOf` over the five constraint slots, and a `not: required` per near
miss — so both fixtures are schema-invalid as well as decoder-rejected. The SECOND is a relation
between two sibling values, which Draft 2020-12 cannot express, so it joins
`schemaInexpressibleRejects` beside `reject-daterange-unordered` for the identical reason. Corpus:
`nodes/form-field-rules.json`, `reject/reject-fieldrule-empty.json`,
`reject/reject-fieldrule-length-unordered.json`, `reject/reject-formfield-near-miss-validation.json`.

**A declared rule is a normative obligation, and it is NOT a security boundary.** `WIRE_FORMAT.md`
states it as a semantic invariant rather than DOM or byte parity (the §22 pattern) and splits it by
host class: a codec host round-trips it, a rendering host must not submit while a rule is unmet and
must show which field is unmet, and a static emitter projects it into the platform's own constraint
attributes and records a known limit where the platform has none (`compare` has none in HTML). Client
enforcement is an affordance; the trust floor is the server-side re-check in
`Fuaran.UI.ServerDriven.FormValidation.enforceDeclared`, which the rule joins rather than inventing a
second enforcement layer beside.

**Validator.** Three new pre-emit codes, one Error and two Warnings, and the asymmetry is the
refuse-only-what-is-provably-wrong rule rather than an inconsistency. `FUARAN099` (Error) — a
`compare` reading a state key no field owns and nothing in the tree writes; decidable from the tree
alone, and it stands down under an opaque writer exactly as `FUARAN103` does. `FUARAN100` (Warning) —
a rule slot the control cannot honour, a `pattern` on a `Checkbox` or a `format` on a `TextArea`;
warned rather than refused because the projection is the host's. `FUARAN101` (Warning) — a `compare`
against a literal duplicating a bound the control already carries; the enforcement half of the reuse
rule above.

**Host adoption.** The F# reference, the shared corpus and the TypeScript tier land together; the
Python, Go and Rust codec legs follow by fixture (the 725 / 730–733 / 801 precedent). The reference is
the only host implementing the three validator codes, so no cross-host message-parity entry applies —
that artefact is scoped to codes at least two non-reference hosts implement.

**Native surfaces are NOT compiler-forced by this change, and that is the finding rather than an
omission.** The reject-corpus floor and the `FormFieldKind.DateRange` precedent both turn on a new
CASE in a closed enum — Swift switches every case with no `default:` and Kotlin's `when` over a sealed
enum is an exhaustiveness error. This change adds no case, so neither host's switch moves and neither
default-deny floor is touched. What they owe instead is a decode-side projection of an optional record
field, which is a strictly smaller obligation.

**Authoring veneers not extended.** The C# fluent factory and the VB XML mapping still author a field
without the rule slot, and both pass `None` at the construction site the new record field forced.
`WIRE_FORMAT.md` §11 step 6 is written for `NodeKind` additions and its gates pin KINDS, so a
spec-record FIELD addition binds neither veneer and neither gate fails — the 801 ruling, applied
unchanged. Exposing the slot there is a follow-up, recorded here so it reads as a decision rather than
an oversight.

## Recorded breaking change — 0.37.0, the retired positional slot becomes a decode error (fuaran#687)

The close of the migration window 0.4.0 opened. `JsonDecode.decodeOp` now **REFUSES** a legacy
`position` on `InsertChild` and `newPosition` on `MoveNode`, returning `WRONG_TYPE` at `$.position` /
`$.newPosition` with a didactic naming `ReorderChildren`. Through 0.4.0–0.36.x the field was accepted
and ignored so the five hosts could adopt independently; every host is now positionless and no
emitter in the estate produces it, so the tolerance is withdrawn.

**Breaking by the wire test, not the API test.** No type, DU arity or signature moves — the change is
entirely in what the decoder accepts. A persisted `0.3.x` op-stream that was still replaying through
the tolerance stops replaying, which is precisely the point: it was applying as an append, so it was
already not doing what its ordinal asked.

**Closing the window meant ADDING a refusal, not removing an acceptance — and that asymmetry is the
whole design.** The decoder reads named fields and ignores the rest, so *not reading* `position` was
the tolerance; there was never a read to delete. A host that merely stopped mentioning the field
would have gone on accepting it forever, indistinguishable from one that had never adopted. The
refusal is therefore explicit and BY NAME, on the enumerated-near-miss pattern (`checkNearMisses`,
0.31.0's `FormField` rule slot): §2 rule 2's tolerance of genuinely-unknown keys survives, because a
slot a future profile may add must stay addable.

**The refusal is ordered AHEAD of the required-field decode**, matching the `FormField` near-miss
ordering, so an op carrying both a retired ordinal and another defect names the ordinal. Without
that, an author would fix the other defect and meet this one only on the next run. The ordering is
fixed identically across all five hosts, so which defect surfaces first is deterministic.

**Host adoption.** The F# reference, the shared corpus and the TypeScript, Python, Go and Rust codec
legs land together — a decoder that still accepts the field while the corpus calls it a reject is
non-conformant, so this is not a fixture-follows precedent. Two new reject fixtures
(`reject-op-insertchild-retired-position`, `reject-op-movenode-retired-newposition`) certify it; both
payloads are otherwise well-formed, deliberately, so a host that merely fails them earlier for some
other reason certifies nothing.

**`schema.json` mirrors it structurally** — each op case gains `allOf: [{ not: { required: [<field>] }}]`,
forbidding by name rather than by `additionalProperties: false`, so the schema and the decoders agree.

**Native surfaces are unaffected, and that is a finding rather than an omission.** `fuaran-swift` and
`fuaran-kt` carry **no `TreeOp` decoder at all** — they hand canonical op JSON to the Rust core, which
owns op decoding — so the Rust leg is their adoption. Neither repo needed a change.

---

## Recorded change — 0.38.0, the host-declared kind admission policy (fuaran#1020)

**Additive on every axis, and the wire does not move.** No fixture payload changed, no encoder path
changed, and `--emit-corpus` regenerated the corpus byte-for-byte apart from the derived
`validator/defect-vocabulary.json` entry for the one new defect below. The mechanism is a NEW OPTIONAL
ARGUMENT at the decode boundary; a decoder given nothing behaves exactly as 0.37.0 did.

**Why it exists.** Every escape hatch in the vocabulary is registration-gated at RENDER time, so an
application that registers no custom renderers and installs no guest seam is closed by omission. That
closure is invisible — nothing states it, so nothing can check or claim it — and it is not monotone:
it stops holding the day an unrelated registration lands elsewhere in the process, with no change to
any tree. A declared policy makes the closure a checkable property of the deployment and the refusal
an attributable event.

**The new specification section is [`WIRE_FORMAT.md` §23](../wire-format-fixtures/WIRE_FORMAT.md),
and §22 is unqualified by it.** A tree carrying a hostile payload is still a valid wire document that
a default decoder MUST NOT reject; §23 narrows one HOST'S acceptance, never the vocabulary.
Conformance for every other family is measured with no policy declared, so a document refused under a
policy is not thereby malformed.

**Additive public surface (each a *minor* event per the Semver note above).**

- New module `Fuaran.UI.KindPolicy`: `DecodePolicy` (identity + `Admission`), `Admission`
  (`AdmitAll` / `AdmitOnly of Set<string>`), the `DecodePolicy` companion
  (`admitAll` / `admitting` / `excludingFrom` / `admits` / `narrows` / `hint`), and `wireKindName`.
  It sits in `Fuaran.UI` rather than beside the decoder because both ends consume it and
  `Fuaran.UI.Ops` depends on this package, never the reverse — the same placement argument
  `WireLimits` carries.
- `JsonDecode` gains `decodeNodeWithPolicy` / `decodeNodeObjWithPolicy` / `decodeOpWithPolicy`, plus
  `hatchNodeKinds` and `JsonDecode.Policy.{closedProfile, excluding}`. **The three existing entry
  points are unchanged in signature and behaviour** — they are these three at `DecodePolicy.admitAll`,
  which makes "the default is unchanged" a property of the code rather than a claim about it.
  Sibling entry points rather than an optional parameter because F# optional parameters exist only on
  type members: defaulting in place would mean reshaping the whole decoder module into a class, a
  break on every consumer, to buy syntax. The TypeScript host, whose language has the construct, may
  take the optional-argument form; what the hosts owe each other is the same behaviour on the same
  bytes, not the same arity.
- `JsonDecode.DecodeErrorCode` gains **`KIND_NOT_ADMITTED`** — the eighth code, on the 0.28.0
  `LIMIT_EXCEEDED` precedent (a new code is a minor event here; changing an existing code string is
  not). It is unreachable without a declared policy, and it is deliberately NOT `WRONG_NODE_KIND`:
  that code means the vocabulary has no such kind, this one means the kind exists and this deployment
  declines it, and an author repairs them differently.
- `PreEmitValidate` gains `KindNotAdmitted` → **FUARAN104 (Warning)** plus `validateWithPolicy` and
  `validateWithRegistryAndPolicy`. Advisory at the authoring end by design: an authoring host may
  legitimately build a tree for a different deployment under a different policy, and the decode
  boundary is where a policy is enforced. `validate` and `validateWithRegistry` declare no policy, so
  the finding is unreachable through them.

**The recommended closed profile excludes `Custom` and `Mount`, and closes nothing else.** Those are
the two kinds through which host-supplied behaviour enters a rendered tree. A kind gate does not reach
the action vocabulary, a declared field rule's pattern, a renderer's output, or anything a host
registers outside a tree — §23.5 states the limit, because a partial closure read as a total one is
worse than none.

**Ops are gated on the same terms.** A kind reaches a `TreeOp` through a node-bearing arm and through
`EditNode`'s replacement kind; both refuse. A policy enforced only on the initial tree would be a
property of the first decode rather than a closure.

**Host adoption.** The F# reference and the shared corpus land together; the
[`decode-policy/`](../wire-format-fixtures/decode-policy/) family is hand-authored (the
`sanitization/` posture) rather than emitted, because a case pairs a document with a DECLARATION the
document does not carry, which the generated reject machinery structurally cannot express. §23 is
**optional** for a host — unlike §21 or §22 — so a host that has not implemented it declares the
family not-applicable with a reason rather than being non-conformant. The TypeScript leg is filed
separately.

## Recorded change — 0.39.0, the silent-zero derivation warning (fuaran#865)

**Additive on every axis, and the wire does not move.** No fixture payload changed, no encoder or
decoder path changed, and `--emit-corpus` regenerated the corpus byte-for-byte apart from the derived
`validator/defect-vocabulary.json` entry for the one new defect below. Nothing about how a document is
read or resolved is different; a new Warning is raised about a document shape that already decoded and
already rendered exactly as it does now.

**Why it exists.** `Binding.State`'s `defaultValue` is a PER-READER FALLBACK, not a slot seed: nothing
writes it into the store. So a `Binding.Transform` whose own source is a default-less
`Binding.State{key}` derives its initial snapshot from `TransformLive.emptySource`, and with no writer
anywhere in the tree the channel never changes — a `groupBy`/`count` over it renders zero, forever,
with nothing red at decode, at validate, or at render. The shape is idiomatic and looks correct: a grid
carrying rows on its own `defaultValue` beside a badge counting "the same" rows. It is the strongest
form a validator finding takes — the emission is what the language teaches, and the language makes it
unsound.

**This is a defect FLAG, not a semantics change, and the distinction is the whole scope of the
release.** The seeding rule that would make the two-node document mean what it looks like it means
(`Binding.State.defaultValue` seeding the slot at mount) is DEFERRED — it re-means an already-shipped
document, which every conformant host would have to adopt simultaneously, and the evidence for it is
one criterion at ×2. See
[`docs/domain-explorations/shared-data-source-charter.md`](../docs/domain-explorations/shared-data-source-charter.md)
§10. Nothing here anticipates it.

**Additive public surface (each a *minor* event per the Semver note above).**

- `PreEmitValidate` gains `TransformSourceInert of nodeId * key` → **FUARAN105 (Warning)**. Raised by
  the existing `validate` / `validateWithRegistry` / `*WithPolicy` entry points; no new entry point.
- `BindingWalk.BindingUse` gains `TransformStateSource of key * hasDefault`, and
  `BindingWalk.StateKeyFacts` gains `TransformInertSources: (string * string) list`. A DU case and a
  record field are both construction-site breaks in F# — the 0.36.0 precedent for the identical shape,
  and why this is a minor rather than a repack.

**A sibling reader's default is deliberately NOT a rescuer.** Under the shipped resolver it never
reaches the Transform, so standing the rule down on one would be reading the tree under a seeding
semantics the language does not have — and would make the rule silent on precisely the pair it exists
to name. The charter's §6 wording ("no reader in the tree seeds that key") describes the rule as it
would read AFTER the deferred seeding change; under the shipped semantics the decidable subject is the
Transform's OWN source slot. The go-red partner in `Fuaran.UI.Tests` pins that reading.

**Warning, never Error, and it stands down under any opaque writer** — the FUARAN103 posture, for the
same reason: a closure produces an arbitrary action at dispatch time, and a host may populate the key
directly, so "nothing can fill this" stops being provable. Host-reserved keys (the Phase 782 prefix)
are exempt.

**Host adoption.** The F# reference and the shared corpus land together. The TypeScript host declares a
NAMED abstention rather than a silent gap: its pre-emit walker has no binding traversal and no
tree-wide write projection, and a partial port of a rule that reasons from an absence would
false-accuse rather than under-report. See its `validator-coverage.json`.

## Recorded change — 0.26.0, the grid's declarative behaviour slots (fuaran#861, #862, #863)

**Recorded retroactively by fuaran#873, and the delay is itself part of the record.** These three
phases shipped their wire additions on 2026-08-16 and none of them wrote an entry here, so the
version that carries the whole grid-behaviour vocabulary has been undescribed since. None of the
three CUT 0.26.0 either — the version was already standing when the first landed, so three
public-contract additions rode a number no one of them advanced. Under the producer-side rule a
public-contract change advances `<Version>` in the same commit; what actually happened is closer to
three changes sharing one slot. Nothing downstream broke, because the three landed within hours of
each other and the published tier has since moved to 0.39.0 carrying all of them — but a consumer
reading this file could not have learned that 0.26.0 means anything more than the release before it.

**One rule decides all three**, and it is Phase 860's charter: *a grid behaviour the user drives is
declared as a named State key that the grid both writes and reads, carrying a descriptor whose shape
the specification fixes; the affordance belongs to the renderer.* That is why there is no `sortable`
or `pageable` boolean on the grid — the KEY is the affordance. A flag with no key behind it is the
decorative-pager shape the charter exists to refuse, and eleven cross-family emissions of exactly
that shape were what produced it.

**Additive public surface (each a *minor* event per the Semver note above).**

- `DataGridSpec` gains `SortStateKey: string option`, `DefaultSort: DefaultSort option`,
  `PageSize: int option`, `PageStateKey: string option` and `EditStateKey: string option`.
- `ColumnErased` gains `Sortable: bool option` and `Editable: bool option`.
- Every one of the seven is an F# record field addition, so every one is a construction-site break —
  the same shape as 0.36.0's and 0.39.0's, and minor for the same reason.

**Wire contract.** All seven are omitted when absent, so a pre-861 document encodes byte-for-byte as
it did. `sortStateKey` names the key carrying `{"column": <index>, "direction": "asc"|"desc"}`;
`pageStateKey` names the key carrying `{"page": <1-based int>}` and `pageSize` is refused below 1
(`WRONG_TYPE`); `editStateKey` names the key an edited cell's whole updated rows value commits to.
The two column flags NARROW and never widen: absent inherits, `false` opts out, and `true` under a
grid that grants nothing is a pre-emit error — `FUARAN094` on the sort side, `FUARAN095` on the write
side. Corpus: `nodes/grid-bound-sort.json`, `nodes/grid-paged.json`, `nodes/grid-paged-sorted.json`,
`nodes/grid-declared-edit.json`.

**What `editStateKey` actually bought, stated because it is easy to read as a convenience.** Before
it, a DECODED editable grid could not say where its edits land — the only spelling was a closure,
which erases to `"<closure>"` — so the destination survived authoring in F# and vanished on the wire.
That is census row #27's whole complaint.

**Authoring surfaces.** None of the three reached the C#, VB or F# typed AUTHOR facades when it
shipped, and nothing failed: the §11 step-6 gates reflect `NodeKind` CASES, and these are FIELDS.
Closed by fuaran#873 at 0.40.0 below.

## Recorded change — 0.39.0, trend polarity (fuaran#867)

**Recorded retroactively by fuaran#873.** Phase 867 Part B landed `MetricSpec.trendPolarity` into a
0.39.0 that fuaran#865 had cut hours earlier for FUARAN105, and wrote no entry here — so the entry
above describes 0.39.0 as the silent-zero derivation warning and nothing else, while the published
0.39.0 also carries a new wire field and a new bare-string enum. Both changes reached consumers in one
coherent published version, so no contract was swapped underneath anyone; what was wrong is that this
file said only half of what that version means.

**Additive public surface (a *minor* event per the Semver note above).**

- A new bare-string enum type `TrendPolarity` with cases `HigherIsBetter` and `LowerIsBetter`.
- `MetricSpec` gains `TrendPolarity: TrendPolarity` — TOTAL, not an option, with `HigherIsBetter` as
  the identity default and omitted at that default. A record field addition, so a construction-site
  break in F#, and it broke the C# fluent veneer and the poc builder outright, both of which construct
  `MetricSpec` through the generated POSITIONAL constructor. That is the sharpest available statement
  of what step 6's gates do and do not cover: the TESTS are insensitive to a field, the CODE is not.

**Wire contract.** `"trendPolarity": "LowerIsBetter"`, omitted at the default. `Neutral` is RESERVED
and deliberately not a case — which is the whole reason the slot is an enum rather than
`inverted: bool`, since a later admission is then a bare-string addition and not a type replacement.
The composition rule is the deliverable rather than the field, and it is normative at
`WIRE_FORMAT.md` §3.6.1: **sentiment = sign(trend) x polarity**, rendered on the trend element alone.
It never negates the value, and it never writes to `tone` — `tone` says how the reading STANDS,
polarity says which way the quantity IMPROVES, and no host derives either from the other. Corpus:
`nodes/metric-inverted-polarity.json`.

**What Part A fixed, which is why the slot means anything.** Before it, `.fuaran-metric-trend` carried
one class and the reference stylesheet painted it success-toned unconditionally — so a -7.34% error
rate read green by accident and a -7.34% revenue read green while being wrong. A polarity slot shipped
alone would have changed nothing observable. Sentiment is now a function of the resolved trend's sign
across all four reference renderers, and because colour alone fails WCAG 1.4.1 a visible glyph carries
an `aria-label` naming the sentiment — placed on the GLYPH, so assistive technology hears "improving
-7.3%" instead of losing the number to an overriding label.

**Host adoption is PARTIAL and named.** The F#, TypeScript, Swift and Kotlin surfaces read the field.
Go, Python and Rust still paint the trend unconditionally and do not read it — recorded in
`docs/VOCABULARY.md` where the next host sweep will find it, with the corpus fixture that now exists to
gate the port.

## Recorded change — 0.40.0, the wave's authoring surfaces (fuaran#873)

**No wire change, and that is the point.** Nothing about how a document encodes, decodes or resolves
moves; `--emit-corpus` produces the corpus byte-for-byte. What changes is that the seven grid fields,
the field rule and the polarity enum recorded in the two entries above become AUTHORABLE from the
typed F# facade, the C# fluent veneer and the VB XML literals — which none of them was.

**Why this needed a phase of its own.** Phase 801 recorded the mechanism and the honest consequence:
the C# coverage test reflects `NodeKind` union cases and the VB analyzer pins the kind vocabulary, so
a payload-FIELD addition binds NEITHER veneer — no gate fails, and the absence is invisible to every
test. Only a C#, VB or F# author discovers it, in some later session. Five phases then shipped fields
behind that blind spot. The gates are unchanged and still correct for what they measure; what this
release adds is the surfaces themselves, plus hand-written checks in both veneer conformance harnesses
that assert what no reflection over the kind set can notice.

**Additive public surface (each a *minor* event per the Semver note above).**

- `Types.GridSpecOf` gains `SortStateKey`, `DefaultSort`, `PageSize`, `PageStateKey`, `EditStateKey`;
  `Types.Column` gains `Sortable: bool option` and `Editable: bool option`. Record field additions, so
  construction-site breaks — but `Defaults.grid` / `Defaults.column` carry them, so the documented
  `{ Defaults.grid with ... }` idiom is unaffected.
- `Fuaran.UI.CSharp` gains the value records `DefaultSort`, `FieldRule` and `CompareRule`, and the
  enums `TrendPolarity`, `SortDirection`, `TextFormat` and `CompareOp`.
- `DataGridOptions<TRow>` gains the five behaviour slots; `TableOptions` gains Phase 801's `Sortable`
  and `DefaultSort`; `MetricOptions` gains `TrendPolarity`; `Column` gains the fluent narrowing
  methods `Sortable(bool)` and `Editable(bool)`; every `FormField` factory gains an optional
  `FieldRule? rule` parameter.
- The VB tier reads them as attributes: `sort-state-key` / `page-size` / `page-state-key` /
  `edit-state-key` / `default-sort-column` / `default-sort-direction` on `<DataGrid>`, `sortable` and
  `editable` on `<Column>`, the `rule-*` family on `<Field>`, and `trend` + `trend-polarity` on
  `<Metric>`. The analyzer's per-element attribute table gains all of them, so authoring one no longer
  draws FUARAN061.

**`<Metric trend>` arrives with `trend-polarity`, and that pairing is deliberate.** The VB tier had
never carried `trend` at all, so polarity alone would have been a fake affordance in exactly the sense
Phase 866 defines: a statement ABOUT a trend, on a tile that cannot show one.

**Phase 934's `reorderable` is NOT surfaced**, and its absence is a finding rather than a scope
choice: it is the same step-6 follow-up one phase later, and it belongs to that phase.

**What is deliberately NOT authorable, per item.** A grid's per-cell `Editable` / `Checkbox` /
`Button` / `Link` / `Pill` / `Progress` kinds stay F#-only — each is defined by a closure over the
row, which the veneers cannot model and the wire cannot carry. That is the pre-existing boundary these
additions do not move: everything added here is DATA, which is exactly why it survives the veneer
intact, and it is the same reason Phase 750's declarative `TonedPill` could be surfaced when `Pill`
could not.

## Recorded change — 0.41.0, `Binding.State` slot seeding (fuaran#1075)

**This is a SEMANTICS change to a shipped slot, and it is the first entry in this document that is.**
Every recorded change above is additive: an existing document keeps meaning exactly what it meant, and
what grows is what a document *can* say. This one is different in kind. `Binding.State`'s
`defaultValue` stops being a per-reader fallback and becomes a **slot seed** — the declared value is
the value of `$state.<key>` for every reader in the tree, not only for the binding that carries it.
A document that decoded yesterday decodes to the identical tree today and **renders a different
value**, wherever two readers share one key.

**Version decision: MINOR, and the reasoning is recorded because the rule that produced it is not the
one the entries above use.** Those grade on the public *surface* — a record field added is a
construction-site break, so minor. This change's surface additions grade the same way (below), but
they are not why it is a minor: the behaviour change is. Under the pre-1.0 caveat at the head of this
document every minor may break, and re-meaning a slot is exactly what that caveat covers; a patch
would understate it, and a major would say the package's contract had been redrawn, which it has not
— one rule inside one binding case now composes across readers. Consumers pinning an exact patch, per
the same caveat, take it deliberately.

**Why it exists.** The 0.39.0 entry above flags the defect and names the fix as deferred: a grid
carrying rows on its own `defaultValue`, beside a badge deriving a count over the same key, is the
idiomatic shape the pack teaches and the wire made it unsound — the badge derived over an empty table
and rendered a plausible, permanent, wrong answer. That deferral was evidence-gated on a second
corroborating window, which fuaran#872's sweep supplied (a third model family reproducing the
unlinked-copies complaint post-teaching, on a criterion teaching cannot move). The operator admitted
the rule on 2026-08-27 under the charter's own pre-registered reopening condition. The design is
settled in
[`docs/domain-explorations/shared-data-source-charter.md`](../docs/domain-explorations/shared-data-source-charter.md)
§4–§6 and normative in `WIRE_FORMAT.md` §24.4–§24.6.

**The rules, in the form a consumer needs them.**

- **Who declares:** any `Binding.State` with a present `defaultValue`, in any slot. No new
  declaration form and no new namespace.
- **Precedence:** a host-furnished value, then a written value, then the seed. A seed is the value
  before anything else has said anything — never an override — so 0.39.0's "writing wins over
  defaulting" is unchanged.
- **Order-independence:** seeding runs over the whole tree before any binding resolves, so a reader
  that appears before the declaration is not a special case.
- **Two declarations of one key:** identical values agree; different values are FUARAN106 (Error).
  The renderer still has to render, and takes the FIRST in tree order, so every host agrees while the
  validator names the disagreement. An EMPTY table declaration (`"defaultValue": []`) declares
  nothing — it is the value an unseeded slot already has.
- **Host-reserved keys are never seeded.** A seed is a tree-originated write, and Phase 782's `host.`
  floor refuses those on every path.

**What a consumer must check before adopting.** Only one shape changes: a tree in which one reader
declares a value for a key AND another reader reads the same key. Everywhere else — one reader per
key, or a key the host populates — resolves byte-identically to 0.40.0. A host that DEPENDED on the
old per-reader isolation (two nodes deliberately reading one key name with different defaults,
expecting each to keep its own) now gets the first declaration for both, and FUARAN106 names it as an
Error at pre-emit rather than leaving it to be discovered in the rendered output.

**Additive public surface (each a *minor* event per the Semver note above).**

- `PreEmitValidate` gains `ConflictingStateSeeds of key * firstNodeId * secondNodeId` →
  **FUARAN106 (Error)** and `DuplicateInlineTable of firstNodeId * secondNodeId * seedKey` →
  **FUARAN107 (Warning)**, both raised by the existing `validate` entry points; no new entry point.
- `BindingWalk.BindingUse` gains `StateSeed of key * value * fingerprint` and
  `InlineTable of table * seedKey`; `BindingWalk.StateKeyFacts` gains `Seeds: StateSeedDecl list` and
  `InlineTables: InlineTableDecl list`; the record types `StateSeedDecl` / `InlineTableDecl` and the
  functions `BindingWalk.stateSeeds` / `BindingWalk.isEmptySeed` are new. DU cases and record fields
  are both construction-site breaks in F# — the 0.36.0 / 0.39.0 / 0.40.0 precedent.
- `Fuaran.UI.Renderer.Render` gains `withStateSeeds`, the client tier's seeding entry. The server
  tier seeds inside `mkContextWith`, so its public surface is unchanged.

**One shipped rule was WIDENED rather than left alone, and leaving it would have been a
contradiction.** FUARAN105 (0.39.0) fires where a Transform derives over a State key that cannot be
filled. Its shipped wording keyed on the Transform's OWN source slot, and 0.39.0's entry says in terms
why: under the per-reader fallback a sibling's `defaultValue` never reached the Transform, so standing
down on one would have silenced the rule on precisely the pair the charter was written about. Under
seeding the opposite is true, so the rule now stands down when ANY reader in the tree seeds the key —
the wording the charter always gave it. Without that widening the resolver would say the slot is
filled while the validator said it never could be.

**Wire and corpus.** No fixture payload moved: `--emit-corpus` into a scratch directory regenerated
the corpus byte-for-byte apart from the one new fixture and the derived
`validator/defect-vocabulary.json` entries. `nodes/shared-source-seeded-pair.json` is added as the
executable form of §24.6 — one declared table, two readers.

**One cross-host divergence was found and closed in the same change.** The reference host has read
`"defaultValue": []` in a Transform's source slot as the empty table since 0.23.1; that leniency was
never written into `WIRE_FORMAT.md` and never pinned by a fixture, and the TypeScript host refused the
same document. It is now §16's row, the TypeScript decoder accepts it, and the new fixture pins it.
The spelling matters here because it is how a Transform source says "I read this key and carry no data
of my own" — a bare `{"$type":"State","key":k}` wrapper remains refused, and widening the decoder to
accept it is filed separately.

## Recorded change — 0.42.0, `ImageSpec` presentation slots (fuaran#1077)

**Additive, and additive on both boundaries.** `ImageSpec` gains `Fit: ImageFit`,
`AspectRatio: ImageAspect` and `Loading: ImageLoading`, each with an identity default that IS
today's behaviour — `Natural` / `Natural` / `Eager` — and each omitted-when-default on the wire.
So a document written before this release decodes to a tree that renders exactly as it did, and
re-encodes to the bytes it already had. The claim is executable rather than asserted:
`nodes/image-1.json` was not touched by the phase, and the corpus regeneration left it
byte-identical.

**Version decision: MINOR, on the standing record-widening precedent.** No wire byte of any existing
document changes and no shipped slot is re-meant, so the wire contract is untouched. What breaks is
F# **construction sites**: a record gaining required fields stops `{ Src = …; Alt = …; Variant = … }`
from compiling, exactly as at 0.36.0 / 0.39.0 / 0.40.0 / 0.41.0. Authors using
`{ Defaults.image with … }` — the documented form — are unaffected.

**Surface added.**

- `Fuaran.UI.Types` re-exports `ImageFit` (`Natural | Cover | Contain`), `ImageAspect`
  (`Natural | Square | FourThree | ThreeTwo | SixteenNine`) and `ImageLoading` (`Eager | Lazy`),
  all generated from the IDL.
- `ImageSpec` gains the three fields; `Defaults.image` carries all three at their identity default.
- The C# authoring veneer's `ImageOptions` gains `Fit` / `AspectRatio` / `Loading` (defaulted, so
  existing C# call sites keep compiling), with C#-native enum mirrors; the VB XML veneer gains the
  `fit` / `aspect-ratio` / `loading` attributes.
- `Fuaran.UI.Ops.Apply`'s field-level `UpdateProp` surface for `Image` gains `"Fit"`,
  `"AspectRatio"` and `"Loading"`.

**Render.** Both renderers map the tokens to classes and nothing else — `fuaran-image-fit-{cover,
contain}` and `fuaran-image-aspect-{square,four-three,three-two,sixteen-nine}`, with **no** class
emitted at `Natural` on either axis — and emit `loading="lazy"` only under `Loading = Lazy`. No
value from the tree reaches a style attribute. The reference stylesheet gains the matching
`aspect-ratio` rules, which is what makes the reservation hold with CSS alone: the box is sized in
the first layout pass, before the image bytes arrive, so server-rendered output stops shifting when
they land.

**Wire and corpus.** `WIRE_FORMAT.md` §3.6.2 states the rules; the generated §3.2 / §3.5 / §3.6
tables and `idl.json` / `schema.json` were regenerated. Three fixtures added:
`nodes/image-presentation-1` (all three off-default), `lenient/lenient-image-explicit-defaults`
(the explicit-default input canonicalising to the omitted form — the identity defaults stated out
loud), and `reject/reject-unknown-image-aspect` (the CSS ratio spelling `"16/9"`, refused at
`$.kind.aspectRatio` with no `.$type` suffix, per §6's bare-enum rule).

## Recorded change — 0.43.0, `ImageSpec.Caption` (fuaran#1078)

**Additive, and additive on both boundaries.** `ImageSpec` gains `Caption: TextSource option`,
omitted from the wire when `None` (rule 4 — the ordinary optional-field posture, not an identity
default: a caption is content, and there is no default caption the way there is a default fit). A
document written before this release decodes to `Caption = None`, re-encodes to the bytes it already
had, and renders the same bare `<img>` it always did. `nodes/image-1.json` was not touched by the
phase and came back byte-identical from the corpus regeneration.

**Version decision: MINOR, on the standing record-widening precedent.** No wire byte of any existing
document changes and no shipped slot is re-meant. What breaks is F# **construction sites**: the
record gains a fourth required field, exactly as at 0.36.0 / 0.39.0 / 0.40.0 / 0.41.0 / 0.42.0.
Authors using `{ Defaults.image with … }` — the documented form — are unaffected.

**Surface added.**

- `ImageSpec` gains `Caption`; `Defaults.image` carries `Caption = None`.
- The C# authoring veneer's `ImageOptions` gains a nullable `Caption` (defaulted, so existing C#
  call sites keep compiling); the VB XML veneer gains the `caption` attribute.
- `Fuaran.UI.Ops.Apply`'s field-level `UpdateProp` surface for `Image` gains `"Caption"`, taking
  the `CalloutSpec.Heading` shape — a `null` clears it, a value sets it.

**Render.** With a caption, both renderers wrap the `<img>` in
`<figure class="fuaran-image-figure">` and emit the resolved text inside
`<figcaption class="fuaran-image-figure-caption">`. Nothing moves onto the `<figure>`: the a11y
projection, the egress marker and the sanitised `src` stay on the element they describe. Without a
caption there is **no wrapper element at all** — the renderers return the `<img>` expression
untouched, which is why the pre-phase emission is preserved by construction rather than by
matching. The reference stylesheet gains two rules, of which the `margin: 0` reset is the
load-bearing one: a UA stylesheet indents `<figure>` by 40px, so a captioned image would otherwise
sit visibly out of line with the uncaptioned one beside it.

The caption is a full `TextSource` rather than a string, so it is i18n-capable on exactly the terms
every other authored string is — no caption-specific resolution path exists, and
`nodes/image-caption-i18n-1` is what keeps a host from narrowing the slot to a string.

**Wire and corpus.** `WIRE_FORMAT.md` §3.6.3 states the rules; the generated §3.2 / §3.5 / §3.6
tables and `idl.json` / `schema.json` were regenerated. Three fixtures added:
`nodes/image-caption-1` (a `Literal` caption, differing from `nodes/image-1` in exactly one key),
`nodes/image-caption-i18n-1` (an `I18n` caption with a populated arg bag), and
`lenient/lenient-image-caption-envelope` (the enveloped TextSource form reaching
`caption` by construction).

## Recorded change — 0.44.0, `ImageSpec.SrcSet` (fuaran#1080)

**Additive, and additive on both boundaries.** `ImageSpec` gains `SrcSet: SrcSetEntry list`,
defaulting to `[]` and omitted from the wire at that default, together with a new public record
`SrcSetEntry = { Src: Binding<string>; Width: int }`. A document written before this release decodes
to `SrcSet = []`, re-encodes to the bytes it already had, and renders the same `<img>` with neither
`srcset` nor `sizes`. `nodes/image-1.json` was not touched by the phase and came back byte-identical
from the corpus regeneration for the third phase running.

**Version decision: MINOR, on the standing record-widening precedent.** No wire byte of any existing
document changes and no shipped slot is re-meant. What breaks is F# **construction sites**: the
record gains a fifth required field, exactly as at 0.36.0 / 0.39.0 / 0.40.0 / 0.41.0 / 0.42.0 /
0.43.0. Authors using `{ Defaults.image with … }` — the documented form — are unaffected.

**Surface added.**

- `ImageSpec` gains `SrcSet`; `SrcSetEntry` is a new public type re-exported from `Fuaran.UI.Types`;
  `Defaults.image` carries `SrcSet = []`.
- The C# authoring veneer's `ImageOptions` gains a nullable `SrcSet` sequence (defaulted, so existing
  C# call sites keep compiling) and a `SrcSetEntry` record; the VB XML veneer reads repeated
  `<Source src="…" width="…"/>` children, and `Source` joins the analyzer's structural-element set.
- `Fuaran.UI.Ops.Apply`'s field-level `UpdateProp` surface for `Image` answers `"SrcSet"` with
  `NotSupportedYet` rather than `UnknownField` — the slot exists, and it is reachable through
  `EditNode`, exactly as `Src` is and for the same reason: there is no field-level coercion from an
  untyped `obj` to a binding-bearing structure.

**Render.** A non-empty `SrcSet` emits `srcset="<url> <width>w, …"` plus `sizes="100vw"` on the
`<img>`; an empty one emits neither attribute. Three properties are contractual:

- **Every candidate's `Src` passes the same URL-scheme and egress floor the primary `Src` does**, at
  the same `Media` egress class. A srcset entry is a URL the browser fetches with no user act — the
  class that floor exists for — so a slot that skipped it would be a documented bypass.
- **A refused candidate is DROPPED from the list, not neutered.** The primary `src` must exist, so it
  collapses to the refusal URL; a candidate has no such obligation, and offering a browser a
  rendition guaranteed to fail is worse than offering it one fewer. The primary `src` remains the
  fallback the whole mechanism rests on.
- **Candidates are emitted ASCENDING by width, and that sort is the RENDERER's.** The wire preserves
  authored array order, because a JSON array is ordered data and a codec that silently re-sorted one
  would be normalising authored content rather than canonicalising a representation. The determinism
  the server-rendered output needs holds where it is actually needed. The sort is stable, so equal
  widths keep their authored order across re-renders.

`sizes` is deliberately the single bounded value `100vw`: nothing in the document says how wide the
element will be laid out, and the language has no media-query slot for an author to say so. Admitting
one would put a free-form CSS string on the wire — the escape the `ImageVariant` / `ImageAspect`
token vocabularies exist to close.

**The width floor is a decode rule.** `Width` must be a positive integer. The wire has no
refined-integer type (the `DefaultSort.column` precedent), so the floor lives in the policy decoder
and the published `schema.json` (`minimum: 1`) and is pinned by a corpus reject vector. Zero is
refused as firmly as a negative: a `0w` descriptor is not a small image, it is a candidate a browser
can never select.

**Wire and corpus.** `WIRE_FORMAT.md` §3.6.4 states the rules; the generated §3.2 / §3.5 / §3.6
tables and `idl.json` / `schema.json` were regenerated. Four fixtures added:
`nodes/image-srcset-1` (three candidates, authored DESCENDING so the fixture can tell a
codec that canonicalises order from one that does not), `lenient/lenient-image-empty-srcset` (an
explicit `[]` canonicalising to the omitted form — the missing-list-field decode class stated in both
directions), `reject/reject-image-srcset-nonpositive-width` (`width: 0` on the second entry, refused
at `$.kind.srcSet[1].width`), and `reject/reject-image-srcset-null` (a present `null`, refused —
absence already has a spelling).

## Recorded change — 0.45.0, `ImageSpec.Expandable` (fuaran#1079)

**Additive, and additive on both boundaries.** `ImageSpec` gains `Expandable: bool`, defaulting to
`false` and omitted from the wire at that default. A document written before this release decodes to
`Expandable = false`, re-encodes to the bytes it already had, and renders the same bare `<img>` with
no anchor around it. `nodes/image-1.json` was not touched by the phase and came back byte-identical
from the corpus regeneration for the fourth phase running.

**Version decision: MINOR, on the standing record-widening precedent.** No wire byte of any existing
document changes and no shipped slot is re-meant. What breaks is F# **construction sites**: the
record gains a sixth required field, exactly as at 0.36.0 / 0.39.0 / 0.40.0 / 0.41.0 / 0.42.0 /
0.43.0 / 0.44.0. Authors using `{ Defaults.image with … }` — the documented form — are unaffected.

**Surface added.**

- `ImageSpec` gains `Expandable`; `Defaults.image` carries `Expandable = false`.
- The C# authoring veneer's `ImageOptions` gains a defaulted `bool Expandable`; the VB XML veneer
  reads an `expandable` attribute, which joins the analyzer's `Image` attribute vocabulary.
- `Fuaran.UI.Ops.Apply`'s field-level `UpdateProp` surface for `Image` accepts `"Expandable"` as an
  ordinary bool — the `CodeBlockSpec.Copyable` shape.
- `Fuaran.UI.RenderFidelity`'s `Image` row is promoted out of the trivially-single-tier set: it now
  declares `Sensitive = true` and a `RichTier.ClientOnly` overlay, so `render-fidelity.json` changes
  shape for that kind. A consumer reading that artefact sees a `rich.class` of `clientOnly` where it
  previously saw `none`.
- `Fuaran.UI.Renderer` packs one new content file, `content/fuaran-image-expand.js` — the reference
  enhancement, on the `content/fuaran-reference-tables.js` precedent — and the reference stylesheet
  gains the `.fuaran-image-expand` / `.fuaran-image-lightbox*` rules.

**Render — the contract is the anchor, not the overlay.** Under `Expandable` both renderers wrap the
`<img>` in `<a class="fuaran-image-expand" href="{the sanitised src}" data-fuaran-expandable>`. Three
properties are contractual:

- **The baseline is a REAL LINK.** With no JavaScript the reader clicks the picture and the browser
  opens the full-size asset. The overlay is a client-only post-hydration refinement over that link,
  outside every parity comparison — the Phase 290 / 293 split, applied to an affordance rather than
  to a rendering technique. This is why the wire slot is a `bool` and not an `Action`: nothing
  crosses the dispatch gate, and an `Action` would make every expandable image a dispatch site.
- **A `Src` the egress floor refused emits NO anchor**, and no marker attribute either. The `<img>`'s
  `src` must exist so it collapses to the refusal URL; an anchor has no such obligation, and
  `<a href="about:blank">` is the dead control the design exists to avoid. This is the Phase 1080
  dropped-candidate rule applied to the affordance.
- **With a `Caption`, the nesting is `<figure>` → `<a>` → `<img>`**, `<figcaption>` the anchor's
  sibling. The caption is outside the link target deliberately: it is prose a reader selects and
  quotes, and interactive content inside the element whose job is to *label* the image inverts the
  `<figure>`/`<figcaption>` relationship.

**The overlay honours the Phase 289 Modal contract in full** where a host ships one:
`role="dialog"` + `aria-modal="true"`, an `alt`-derived `aria-label`, a focus trap, `Escape` and
backdrop dismissal, focus restored to the opening anchor, and the rest of the document
`aria-hidden` (restored to its prior value, not blanket-removed). Two implementations ship and both
read `[data-fuaran-expandable]`: the packaged dependency-free `content/fuaran-image-expand.js` and
the `@fuaran-ui/renderer/enhance-expandable` module.

**Two binding-walk omissions closed in the same release, and they are the fixes most likely to change
observed behaviour.** `Fuaran.UI.BindingWalk` (the analysis walk) did not descend `ImageSpec.Caption`
or the `SrcSet` candidates' `Src`, and `Fuaran.UI.Renderer`'s reactive walk did not descend
`Caption`. So a caption bound to a State key was validated by nothing and subscribed by nothing — it
rendered once and never followed its source — and a candidate resolved from a query was subscribed
but invisible to analysis. Both walks now descend both slots, and `WalkConformanceTests` carries a
census row for each. A host relying on either omission (a validator that passed because a caption
binding was unseen) will now see the binding.

**Wire and corpus.** `WIRE_FORMAT.md` §3.6.5 states the rules, including the anchor a rendering host
MUST emit; the generated §3.2 / §3.5 / §3.6 tables and `idl.json` / `schema.json` were regenerated.
Four fixtures added: `nodes/image-expandable-1` (the declaration alone),
`nodes/image-expandable-figure-1` (`expandable` + `caption` + `srcSet` on one node — the gallery
thumbnail, and the case where a host that built its encoder as a chain of `if`s gets the canonical
key order wrong), `lenient/lenient-image-explicit-expandable-false` (an explicit `false`
canonicalising to the omitted form), and `reject/reject-image-expandable-nonbool`
(`"expandable":"true"`, refused at `$.kind.expandable` rather than coerced — a truthiness rule would
have to rule on `"false"` too, and two hosts ruling differently would disagree about whether the
document declares an affordance at all).

## Recorded change — 0.47.0, `Fuaran.UI.Renderer.Web` (fuaran#577)

**A NEW package, plus two additive members on the C# veneer. Nothing existing is removed or
retyped, and no wire byte moves.**

`Fuaran.UI.Renderer.Web` embeds the built `@fuaran-ui/renderer` standalone browser bundle and the
canonical reference stylesheet as static web assets, serves them from `MapFuaranRenderer()`, and
emits the HTML snippet that hydrates a serialized tree. A C# or VB developer adds one
`PackageReference` and gets a live client-side render with **no Node toolchain**: the bundle is
built by maintainers, byte-copied by `scripts/sync-renderer-web.ps1`, and committed.

Public surface: `Fingerprint` (the `EmbeddedFingerprint` record, the `Mismatch` DU, `check`,
`describe`, `toJson`, `parse`, `AuthoringWireProfile`), `Assets` (the `Asset` record, the three
assets, `read`, `fingerprint`, `etag`), `Snippet` (`MountOptions`, `defaults`, `styleLink`,
`scriptTag`, `assetTags`, `mount`) and `FuaranRendererEndpointExtensions.MapFuaranRenderer`.

**Its dependency set is `FSharp.Core` plus the ASP.NET Core shared framework, and that is
defended.** It takes no reference on `Fuaran.UI`, the renderer or the op-stream: it serves assets
and emits HTML, and the caller passes the wire JSON and the vocabulary fingerprint. A consumer
therefore adopts it without inheriting anything else this package believes about versions.

**The stylesheet is LINKED, not copied.** The `<EmbeddedResource>` points at
`src/Fuaran.UI.Renderer/content/fuaran-reference.css` where it lies, so this package is not a
fifth tier copy and there is no drift class here for `-- Css` to gain. Only the browser bundle is
a byte copy across a repo boundary, and it carries a fingerprint sidecar that
`-- RendererWebCheck` (inside `Check`) and `Snippet.mount` (at development time) both read.

### `Fuaran.UI.CSharp` — `EncodeForTransport` / `TryEncodeForTransport` (additive)

`FuaranNode` gains the C# leg of the transport encoder, and the package gains the `LossySlot`
record it reports through. `Encode()` is unchanged: its closure-blindness feeds the op-stream hash
chain and is deliberate there, so which method you call is how intent is declared. Additive —
nothing that compiled against 0.46.0 stops compiling — and it rides the in-flight **0.47.0** with
the entries below rather than taking a slot of its own, `v0.46.0` being the newest tag.

## Recorded change — 0.47.0, the transport encoder and FUARAN112 (fuaran#577)

**One new DU case, one record widening, and four new public members; no wire change, no change to
what any existing emission encodes to, and — deliberately — no change to what `validate` or
`encodeNode` say about any tree they already saw.**

```fsharp
// Fuaran.UI.BindingWalk
type ClosureUse = { Reader: string; Slot: string }                 // new
val closuresOfAction : string -> Action<'Msg> -> ClosureUse list   // new
type TreeBindingFacts = { … ; Closures: ClosureUse list ; … }      // ← the widening

// Fuaran.UI.PreEmitValidate
type PreEmitDefect = … | WireLossyActionClosure of nodeId: string * slot: string   // ← the new case
val validateForTransport : Node<'Msg> -> Result<unit, PreEmitDefect list>          // new

// Fuaran.UI.OpStream.Abstractions.CanonicalJson
type LossyPath = { NodeId: string; Slot: string }                                  // new
val encodeNodeForTransport : Node<'Msg> -> Result<string, LossyPath list>          // new
```

**What it is for.** `Action.Dispatch of 'Msg` carries a host closure, and so do `Action.Call`'s
`onResult` and `Action.ReadFileBody`'s `onRead`. The canonical encoder emits the case's discriminator
and DROPS the payload — a `Dispatch` encodes as `{"$type":"Dispatch"}` — and `"<closure>"` is the
DECODER's reconstruction. So a tree authored for an in-process host and then serialised arrives with
an affordance that renders, fires, and does nothing, and **the emitted bytes carry no trace of the
loss**: nothing downstream can tell a `Dispatch` that lost a message from one that never had a
payload. The encoding side is the last place the question is answerable, which is why the refusal
lives with a second encoder rather than with a decoder rule.

**`encodeNode` is untouched, and that is load-bearing.** Its closure-blindness feeds the hash chain,
where two ops differing only in an opaque `'Msg` hash identically BY DESIGN. Making that path refuse
would break the property it exists to have, so intent is taken from WHICH encoder the author calls:
`encodeNodeForTransport` is the author saying the interaction was meant to survive. A test pins both
halves — that the transport encoder refuses the tree, and that `encodeNode` still emits the same bytes
for it that it always did.

**FUARAN112 (Warning) is the backstop**, reported by the new `validateForTransport` and by nothing
else. `validate` says nothing about a closure, unchanged: an in-process Fable host renders `Dispatch`
correctly and forever, so relevance is the CALLER's to declare — the same shape as
`validateWithPolicy`, where a deployment is named rather than inferred. Warning rather than Error for
the same reason.

**What it does NOT claim.** Only the `Action` DU's three closure slots are judged. Other slots erase
too (`FormFieldKind.onChange`, `TabsSpec.onSelect`, `DisclosureSpec.onToggle`), and are deliberately
out of scope because the renderers' write-back default reconstructs their behaviour from the
control's own writable binding — the closure is lost and the interaction is not. `Binding.Computed`
is likewise untouched; it is FUARAN084's subject. A tree the transport encoder accepts carries no
*unrecoverable* interaction, which is narrower than "nothing about it erased".

**Breaking-change classification: a new DU case (breaks exhaustive matches over `PreEmitDefect`) plus
a record widening (breaks full-literal construction of `TreeBindingFacts`, FS0764).** Both ride the
in-flight **0.47.0** on the same reasoning as the entries below — `v0.46.0` is the newest tag, so
0.47.0 is unreleased and this is part of the same unshipped surface. In-repo, `TreeBindingFacts` has
exactly one construction site (`BindingWalk.collect`), so nothing else moved.

**The corpus moves with it (§11 forward-coupling).** `validator/defect-vocabulary.json` gains the
FUARAN112 entry — generated by reflection over the defect DU, so it required no hand edit — and this
host's `validator-coverage.json` gains the code, its `reference` posture meaning it must implement
every code in the vocabulary. `check-coverage.mjs` and `check-message-parity.mjs` are both green.

**Not done, and recorded rather than assumed: `Action.Dispatch` is NOT marked `[<Obsolete>]`.** The
phase proposed it; this repo sets `TreatWarningsAsErrors=true`, so the attribute is an FS0044 ERROR at
every call site — measured, nine of them across five test files here, all authoring trees that are
rendered in process and are correct as written, plus every downstream Fable host. Suppressing them
would weaken the gate to carry a marker that would then mean nothing. The hazard is instead made
legible where it can be without a false accusation: a doc comment on `Action.dispatch` naming the
constraint and the two remedies, the FUARAN112 rule, and the transport encoder's refusal.

## Recorded change — 0.47.0, payload-language declaration on contract props (fuaran#1107)

**Two record widenings plus five new types on `Fuaran.UI`'s extension-registry surface; no wire
change, no renderer behaviour change, and — asserted, not assumed — no movement in any existing
contract's content hash.**

```fsharp
type PayloadGate = { Gate: string; Version: string }          // + member AsStamp
type PayloadLanguage = { Language: string; Gate: PayloadGate option }

type PropDecl = { … ; PayloadLanguage: PayloadLanguage option }   // ← widening 1
type CustomPropCard = { … ; PayloadLanguage: string option ; PayloadGate: string option }  // ← widening 2

type PayloadObligationKind = GateOwed | Ungated                // a CLOSED two-case DU
type CustomPayloadObligation = { Key; Language; Gate; Kind; Message }
type CustomValidation = { Defects: CustomPropDefect list; Obligations: CustomPayloadObligation list }

type PayloadGateVerdict = Accepted | Refused of reason: string | NotRun
type PayloadUpdateProvenance = { ModuleId; ComponentId; Key; Language; Gate; Verdict }
```

plus `Defaults.propDecl`, the module functions `PayloadLanguage.gated` / `.ungated`,
`CustomRegistry.payloadTag`, `CustomContract.payloadLanguage` / `.payloadProps`,
`PayloadProvenance.forUpdate` / `.attribution`, and two registry members
(`ValidatePayloads`, `ValidatePropsDetailed`).

**What it is for.** A registered contract's payload prop was declared `PString` — "a string" — which
loses the most important fact about it: that string IS a wire format with its own decoder and its own
gate. `points: string` and a whole markdown document were the same declaration, so a payload that was
prose rather than its declared format passed prop validation and failed only at render.

**ANNOTATION, NOT A `PropType` CASE — the decision.** The two facts are orthogonal (`PropType` answers
what JSON shape; the declaration answers what the content means and who judges it, and a payload need
not be a string at all). `PropType` is closed and its closedness is the point — every `matchesType` /
`propTypeTag` arm is exhaustive, and a ninth case would break all of them in every consumer for a fact
none of them needs; not one existing match moved under the annotation. And the set of payload
languages is OPEN — every domain names its own format and gate — so a `PWire` case would carry
free-form strings inside a vocabulary whose whole value is being a fixed checkable list, which is the
typed-surface erosion the bounded-escape posture exists to resist, done to the one type meant to
resist it.

**Breaking-change classification: two record widenings, so full-literal construction of `PropDecl`
and `CustomPropCard` breaks (FS0764).** It rides the in-flight **0.47.0** on the same reasoning as the
entries below: `v0.46.0` is the newest tag, 0.47.0 is unreleased, and a consumer moving 0.46.0 →
0.47.0 sees one minor's worth of change. `Defaults.propDecl` is the Phase-1106 answer for `PropDecl`
and is why the next annotation on a prop schema costs no edit at any authoring site; `CustomPropCard`
is constructed only inside `DescribeForAi`.

**THE CONTENT HASH DOES NOT MOVE, and that is asserted on two independent legs.**
`Hashing.customBodyShapeHash` folds the module and component ids, the prop KEY SET and the exposed ids
— never a prop's declared detail — so a contract that adopts the declaration hashes exactly as it did
before. `CustomRegistryTests` asserts the annotated, unannotated and permissively-derived contracts
over one prop set agree with each other, AND pins the resulting digest to a literal computed outside
this codebase. The first alone compares two calls to the same function and so could not see a change
to the derivation itself; the second alone could not see a schema-detail leak that moved all three
together. A moved hash would invalidate every `StrictReplay` consumer of an existing component for a
change that altered nothing about what it emits.

**An obligation is NOT a defect, and the separation is load-bearing.**
`ValidatePropsDetailed` returns the schema defects (`FUARAN068`, error-grade, unchanged) and the
payload obligations apart from one another. `ValidateProps` is unchanged in signature and in
behaviour, so no existing consumer moves and no contract starts failing validation for the act of
describing itself more honestly — which is the surest way to get a declaration left off. Nothing is
wired into `PreEmitValidate.validateWithRegistry` for the same reason: that path maps the whole
`CustomPropSchemaViolation` case to `FUARAN068`/Error regardless of the per-defect codes it carries, so
an obligation folded in would escalate a tree the registry has no grounds to refuse. No new `FUARAN`
code was allocated.

**Two obligation classes, because the remedies differ.** `GateOwed` — a gate is named and did not run
here; the registry holds no decoder for any domain's format, and the one definition of that gate lives
in the domain that owns the language. `Ungated` — a language is declared and no gate is named, so
nothing can judge the payload at all: a claim with no falsifier. An absent or wrong-shaped payload prop
raises no obligation, because a `FUARAN068` defect already reports it and two reports of one fault make
the obligation list a noisier restatement of the defect list rather than the different question it is.

**Provenance is a SHAPE, not a wiring.** `PayloadUpdateProvenance` + `PayloadProvenance.attribution`
fix the bytes two hosts write for the same fact ("via `<language>` gate `<stamp>` — `<verdict>`"); the
op-stream/telemetry wiring is per-host, because this tier holds no sink and can run no domain gate.
`forUpdate` returns `option` and yields `None` for a prop that declares no language, so an attribution
is falsifiable against the schema rather than being whatever the writer typed. `NotRun` is a
first-class verdict rather than an omission: a stream that leaves the unjudged case out cannot
distinguish "the gate ran and was content" from "nobody looked", and that reading is exactly how an
unjudged payload becomes an assumed-good one.

**fuaran-ts parity is at that tier's own granularity, and the divergence is named.** The TS registry is
a RENDERER registry — it carries no prop schema at all, so there is no prop to annotate. It gains the
same declaration types and the same two obligation classes, declared per-component-per-prop-key on
`register`, plus `describePayloadLanguages()` and `payloadObligations()`. The F# tier derives the
declared set from a schema it already holds; the TS tier is handed it. Same vocabulary, same tags, same
attribution line.

## Recorded change — 0.47.0, render obligations on the fidelity manifest (fuaran#1105)

**A record widening plus two new types on `Fuaran.UI.RenderFidelity`; no wire change, no renderer
behaviour change, and no change to what any existing row already said.**

```fsharp
type ObligationClaim =            // a CLOSED DU of nine checkable claims
    | AccessibleNameAlways | AutoplayMutedPairing | NoAutoplayPathway | RefusedSourceDropped
    | AltAlwaysEmitted | AnchorAffordanceOnExpandable | RefusedSrcNoAffordance
    | FigureCaptionOutsideLink | SrcSetAscendingByWidth

type Obligation = { Claim: ObligationClaim; Statement: string; Section: string }

type FidelityRow = { … ; Obligations: Obligation list ; … }   // ← the widening
```

plus the reader/reporting surface beside them (`claimId`, `claimMeaning`, `allClaims`,
`allObligations`, `ObligationOutcome`, `ObligationReport`, `reportWith`, `unasserted`,
`describeReport`).

**What it is for.** The `Fallback` prose on each row is complete, normative, and unfalsifiable by a
machine. A host can render a kind, pass every byte-parity fixture in the corpus, and still have
silently dropped an obligation that paragraph states — `<audio>` growing an autoplay pathway its case
declares no slot for, `autoplay` emitted without its `muted` pair, an expansion anchor pointing at a
destination the egress floor refused. None of those is a missing discriminator arm, so neither the
codec suite nor the compiler reaches them. The obligations are the CHECKABLE remainder: each names one
consequence a host's render suite can assert in emitted output, bound to the section that states it.
`Media` and `Image` declare them (four and five respectively); every other row declares none, which
says its fallback states no separately-checkable claim — never that its prose is optional.

**Breaking-change classification: a record widening, so full-literal construction of `FidelityRow`
breaks (FS0764).** It rides the in-flight **0.47.0** rather than taking a slot of its own, on the same
reasoning as the entry below: `v0.46.0` is the newest tag, so 0.47.0 is unreleased and this addition
is part of the same unshipped surface as the `Masonry` and TreeOp-vocabulary entries. A consumer moves
0.46.0 → 0.47.0 and sees one minor's worth of change. In-repo construction is through the private
`row` / `plain` helpers, so no call site moved.

**The artefact and the spec move with it (§11 forward-coupling).** `render-fidelity.json` gains a
top-level `obligationVocabulary` (the closed set, so a host can enumerate what EXISTS independently of
the rows it reads) and a per-kind `obligations` array; `WIRE_FORMAT.md` §13 states the mechanism, the
three-outcome reporting shape, and the §11.0 per-host adoption roster. The stale-artefact guard
already pinned the two byte-for-byte and needed no change.

**Pinned on four legs.** `RenderFidelityTests` adds a vocabulary block — a **reflection** check that
`allClaims` enumerates every DU case (the one guard a Fable-safe module cannot state about itself:
`claimId` being exhaustive forces a new case to be NAMED, but not to reach the enumeration the artefact
is emitted from), distinct kebab ids, a statement and a `WIRE_FORMAT.md` section on every obligation,
and the artefact carrying exactly what the table declares. `Fuaran.UI.Renderer.Server.Tests`'
`RenderObligationTests` is the adoption: it enumerates from the generated artefact and asserts all nine
in emitted HTML, prints every claim it does not assert, and fails on any that carries no declared
exemption — verified go-red by declaring a tenth obligation on a kind with no checker.

## Recorded change — 0.47.0, the TreeOp vocabulary as an EXPORT (fuaran#1104)

**Purely additive public surface on `Fuaran.UI.Ops.JsonDecode`; no wire change and no type
change.** Four new values:

```fsharp
val opWireFields : (string * (string * bool) list) list   // tag, (wire field, required)
val knownOpKinds : string list                            // the $type discriminators
val retiredOpFields : (string * string) list              // (op, field) a decoder refuses BY NAME
val unknownOpKindHint : string                            // projected from knownOpKinds
```

**What it replaces.** The op half of the wire vocabulary had no declaration anywhere: its only
enumeration was a pipe-separated literal inside `decodeTreeOpAstCore`'s fallback arm, which is not a
surface. A consumer that needed it — a teaching surface, an authoring surface, another host — had to
either re-type it (the second copy `nodeKindGroups` exists to remove on the node side) or reflect
over `TreeOp`'s union cases, which yields the case names and **nothing else**. Reflection cannot
reach the wire field names, because the DU's own labels are not the wire's: `EditNode of NodeId *
NodeKind<'Msg>` carries no labels at all, and the wire calls those two `target` and `newKind`. The
fallback arm's literal is now projected from `unknownOpKindHint` rather than restating it.

**Version decision: rides the in-flight 0.47.0, no separate bump.** Additive-only — nothing is
removed, renamed or re-meant, so no consumer's construction site or exhaustive match is affected —
and 0.47.0 is unreleased at the time of writing (`v0.46.0` is the newest tag), so this addition is
part of the same unshipped surface as the `Masonry` entry below rather than a second slot to cut.

**Pinned on two independent legs** (`Fuaran.UI.JsonDecode.Tests/OpVocabularyTests.fs`): against the
corpus's `idl.json` — the artefact the structural layer is generated from, which carries the tags,
the wire field names AND their optionality — and against `TreeOp`'s own union cases. The second is
deliberately kept rather than replaced: it reads the SHIPPED type where the first reads the artefact,
so the two disagree if the generated layer and the corpus ever fall out of step, which neither leg
could report alone.

## Recorded change — 0.47.0, `LayoutMode.Masonry` (fuaran#1082)

**Additive on the wire, and a NEW CASE on a closed DU in the type.** `LayoutMode` gains

```fsharp
| Masonry of cols: int * gap: int option
```

with an authoring record and smart constructor beside it:

```fsharp
type MasonryLayoutSpec<'Msg> = { Cols: int; Children: Node<'Msg> list }
Fuaran.masonryLayout : string -> MasonryLayoutSpec<'Msg> -> Node<'Msg>
Defaults.masonryLayout<'Msg>          // Cols = 3
```

No existing document changes meaning: `Masonry` is a discriminator no prior emission carried, and no
shipped slot is re-meant. `Grid` is untouched — same arity, same fields, same bytes.

**Version decision: MINOR**, on the 0.46.0 precedent directly above (and 0.7.0,
`FormFieldKind.DateRange`): a new DU case breaks exhaustive matches rather than construction sites,
and pre-1.0 that rides a minor.

**Why a new CASE and not a field on `Grid` — the counter-intuitive half of the charter's §2.1 rule.**
A fill DIRECTION could have been spelled as a `Grid` field, and that spelling is the *more* expensive
one, not the cheaper: adding a field to a DU case changes that case's arity, so **every** pattern
match on `LayoutMode.Grid` across every host and every consumer stops compiling. A new case leaves
them all alone and raises `FS0025` only where a match is exhaustive. Measured in this repo: **two**
arms needed adding (`Theme.kindClass` and `Introspect.availableFields`), against a whole-estate sweep
the other spelling implied. Four further sites were completed voluntarily rather than by the compiler
— `Apply`, `Tools.extractProps`, `TreeOpDiff`, `Render.nodeKindName` — because each would otherwise
have silently under-reported the new case rather than failing.

**No escape hatch is created.** `Masonry` carries `cols` and `gap` and deliberately no
`templateColumns` twin, so the complete set of CSS properties a masonry container can cause a host to
emit is fixed by `WIRE_FORMAT.md` §3.6.7, and every one of them is a known CSS property.
`GridLayoutSpec.TemplateColumns` — the one layout slot carrying a verbatim CSS string — is unchanged
and unextended: `Masonry` deliberately has no twin of it.

**`cols` carries a positive floor** enforced by the policy decoder (`WRONG_TYPE` at
`$.kind.layout.cols`) and by the published schema (`minimum: 1`), on the 0.44.0 `srcSet` width
precedent. The generated structural layer cannot express it — the IDL has no refined-integer type —
so the fixture lands on the policy side of the `GeneratedLayerTests` accept-set seam, as the third
such value bound before it did.

## Recorded change — 0.46.0, `NodeKind.Media` (fuaran#1076)

**Additive on the wire, and a NEW CASE on a closed DU in the type.** `NodeKind` gains
`Media of MediaSpec`, where

```fsharp
type MediaSpec =
    { Controls: bool           // omitted from the wire at TRUE
      Kind: MediaKind
      Label: TextSource        // MANDATORY — the a11y floor, with no decorative case
      Loop: bool               // omitted from the wire at false
      Src: Binding<string> }

and MediaKind =
    | Video of autoplay: bool * poster: Binding<string> option
    | Audio
```

No existing document changes meaning: `Media` is a discriminator no prior emission carried, and no
shipped slot is re-meant.

**Version decision: MINOR, and the precedent is a different one from the ImageSpec run above.**
0.36.0–0.45.0 were record WIDENINGS, which break construction sites. This is a new DU case, which
breaks **exhaustive matches** — every consumer switching over `NodeKind` gains an arm. The
pre-1.0 precedent for that is 0.7.0 (`FormFieldKind` gaining `DateRange`), and it rides a minor for
the same reason: pre-1.0, and the alternative is a major bump for an addition the format is designed
to absorb.

**One kind, two variants — never two kinds.** The vocabulary charter's Appendix A pre-ruled the shape
(`VOCABULARY.md`, the `Media` row, amended to ADMITTED in the same change-set under a recorded
operator mandate). Everything the two surfaces share lives once on `MediaSpec`; only the video-only
slots live in the variant. Phase 1083, which had specified a second `Audio` kind, is subsumed.

**Surface added.**

- `Fuaran.UI.Types` re-exports `MediaSpec` + `MediaKind`; `Fuaran.UI.Defaults.media` carries
  `Controls = true`, `Kind = Video(false, None)`, `Loop = false`, an empty `Label` and no source.
- `Fuaran.Fuaran` gains `video` / `audio` (positional `id`, `src`, `label`) and `mediaSpec`. The
  label is a REQUIRED positional argument on both: a constructor that let it be omitted would make
  the easiest thing to write the thing FUARAN108 refuses.
- `Fuaran.UI.Ops.Apply`'s field-level `UpdateProp` surface accepts `"Label"` / `"Controls"` /
  `"Loop"`; `"Src"` and `"Kind"` answer `NotSupportedYet` (a `Binding` and a payload-bearing union
  have no coercion from an untyped `obj`).
- `Fuaran.UI.PreEmitValidate` gains **FUARAN108 (Error)** — a `Media` node whose `Label` is an empty
  or whitespace `Literal`. Only a literal is judged: a `Bound` / `I18n` label resolves from data the
  walk cannot see, so calling it empty would be a guess.
- `Fuaran.UI.Renderer.Accessibility.forwardsToSemanticElement` returns `true` for `Media`, so a
  node-level `Accessibility` projection lands on the `<video>` / `<audio>` rather than the wrapper —
  the `Image` treatment, on the same three grounds.
- `Fuaran.UI.RenderFidelity` gains a `Media` row (`Sensitive = true`, `RichTier.None`), so
  `render-fidelity.json` grows one entry.
- The C# veneer gains `Fuaran.Video` / `Fuaran.Audio` + `VideoOptions` / `AudioOptions`; the VB XML
  veneer gains a `<Media kind="video"|"audio">` element, whose attribute vocabulary joins the
  analyzer's table. `AudioOptions` deliberately carries no autoplay and no poster.
- The reference stylesheet gains `.fuaran-media` / `.fuaran-media-video` / `.fuaran-media-audio`.

**Render — three obligations, all normative in `WIRE_FORMAT.md` §3.6.6 rather than advisory, because
a host that got any of them wrong would still round-trip the bytes perfectly.**

- **`aria-label` ALWAYS.** The label is mandatory and a transport has no decorative case, so unlike
  `ImageSpec.Alt` there is no branch a renderer takes when it resolves to nothing.
- **`autoplay` NEVER without `muted`.** The pairing is what the declaration MEANS, not a default a
  caller overrides — which is why the wire carries no separate muted slot to fall out of step with
  it. Every browser blocks unmuted autoplay, so an unmuted emission is a player that silently never
  starts: the declaration would mean nothing and the failure would be invisible. The converse holds
  too — `muted` is not emitted where `autoplay` is absent.
- **The `Audio` variant has NO autoplay pathway**, in the type, on the wire, or in the emission. This
  is stronger than a default of `false`: a slot that defaults to off is one a document can switch on.

**Both URLs pass the §19 egress floor, and a refused POSTER is dropped where a refused `Src`
collapses.** An element must have a source, so `src` takes the refusal substitute and carries its
marker; a poster simply leaves, because a `<video>` with no poster shows its first frame — a working
rendering — while a poster at the refusal URL is a broken image painted over the player. That is the
0.44.0 dropped-candidate rule applied to a single slot.

**No client-only tier, and that is a claim rather than a blank.** A `<video controls>` is already a
complete interactive control in every browser, so no renderer attaches anything at hydration and
there is no enhancement module to ship.

**Wire and corpus.** `WIRE_FORMAT.md` §3.6.6 states the rules; the generated §3.2 / §3.5 / §3.6
tables and `idl.json` / `schema.json` / `render-fidelity.json` were regenerated. Seven fixtures
added: `nodes/media-video-1` (the minimum — both bool defaults omitted, the payload the bare
discriminator), `nodes/media-video-poster-1` (the poster INSIDE the case object rather than beside
it), `nodes/media-video-autoplay-1` (all three bools off their defaults at once, which is where an
encoder built as a chain of `if`s gets the canonical key order wrong), `nodes/media-audio-1`,
`reject/reject-media-missing-label` (`MISSING_FIELD` at `$.kind.label`),
`reject/reject-unknown-media-kind` (`UNKNOWN_DU_CASE` at `$.kind.kind.$type` — the variant is
`$type`-discriminated, so the path carries the suffix a bare enum's does not), and
`reject/reject-media-autoplay-nonbool` (`WRONG_TYPE` at `$.kind.kind.autoplay` — the stringified
boolean refused rather than coerced, on the slot where a truthiness rule would make one host start
playing a video another host leaves still).

---

## Recorded change — 0.47.0, contract cards + the unregistered-degradation obligation (fuaran#1108)

**Two record widenings, one new module, one new obligation claim, and a second renderer store; no
wire change to any existing document, and — asserted, not assumed — no movement in any existing
contract's content hash.**

```fsharp
type CustomContract<'Props> = { … ; Summary: string option }        // ← widening 1
type CardContentHash = { Algorithm: string; Hash: string }          // algorithm + digest, NO strictness
type CustomKindCard = { … ; Hash: CardContentHash ; Summary: string option }  // ← widening 2

type CardHashVerdict = Matches | Unverified of reason: string | Mismatch   // a CLOSED three-case DU
type CardValidation  = { Defects; Obligations; Unresolvable: string list }
type CardPlaceholder = { ModuleId; ComponentId; Label; Summary; PropLines; HashVerdict; Validation }
type CustomCardStore = …                                            // a host-supplied lookup
```

**What this is for.** A registered custom component is first-class inside its own deployment and
opaque everywhere else: a foreign host has no contract, no schema and no renderer, so the most it can
honestly do is name the component and stop. What the issuing deployment had, and the receiver did
not, was never a RENDERER — it was the DESCRIPTION, and `CustomKindCard` already assembled all of it
for the orchestrator's prompt context. Making that card a specified, transportable artefact
(`WIRE_FORMAT.md` §25) turns "opaque elsewhere" into "legible-but-unrendered elsewhere" at a fraction
of the cost of a portable renderer.

**Surface added.**

- `Fuaran.UI.CustomCard` — the format-version tags, the placeholder derivation (`describe`), the
  three-way hash verdict (`verifyHash` / `verdictMarker`), card-driven prop validation (`validate`),
  and the `/.well-known/fuaran-cards.json` path convention. Fable-safe, FSharp.Core + the Core JSON
  model, so both renderers reach it without a decoder dependency.
- `Fuaran.UI.CustomCardStore` — the host-supplied lookup, and deliberately a SECOND store rather than
  a field on the renderer registry. A registry says "I can render this"; a store says "I can describe
  this", and all four combinations are meaningful. Folding cards into the registry would have made a
  description obtainable only where a renderer already existed — i.e. in every case except the one
  the artefact exists for.
- `Fuaran.UI.CustomContract.withSummary` — a combinator, not an eighth positional argument on
  `create` / `createWithSchema`. **The summary does not enter the content hash**, which the tests
  assert rather than leave to inspection: a hash that moved on a reworded sentence would invalidate
  every `StrictReplay` consumer of a component whose emitted shape did not change at all.
- `Fuaran.UI.CustomRegistry.validateSchema` — the registry's judgement, lifted out of the registry so
  the card path and the contract path cannot reach different verdicts. `tryParsePropTypeTag` is the
  inverse of `propTypeTag`, and answers `None` for a tag from a newer producer rather than guessing
  one.
- `Fuaran.UI.Ops.CustomCardJson` — the codec. Here rather than beside the types because it speaks the
  §6 `DecodeError` envelope declared in `JsonDecode`; a card refusal with its own vocabulary would be
  a second refusal shape for hosts to learn and would fit none of the corpus manifest's reject
  columns. `exportBundleJson` is the registry's publication path.
- `Fuaran.UI.Renderer.Server` gains `ServerRenderContext.Cards`, `mkContextWithCards` and
  `renderWithCards`. Empty by default, and an empty store leaves every emitted byte exactly as it
  was.

**The render obligation (`unregistered-custom-labelled`, `WIRE_FORMAT.md` §25.4).** A new
`ObligationClaim` case on the fuaran#1105 vocabulary, attached to the `Custom` row, so an adopting
host's suite enumerates it from `render-fidelity.json` rather than from prose. Where a host renders
an unregistered `Custom` node and a card for its identity is available, the placeholder carries the
identity, the card's summary, and a machine-readable verdict marker — never a prop VALUE and never a
guess. **Where no card is available the placeholder is byte-for-byte what it was**, which is what
makes the obligation safe to declare on a kind every roster host already renders.

**The three-way verdict is the load-bearing detail.** A `moduleId`/`componentId` pair is an ADDRESS,
and two deployments can ship different components at the same address. So `described` (hashes agree)
shows everything; `unverified` (nothing to compare — no declared hash, or different algorithms) shows
everything and says the claim is unverified; and `hash-mismatch` WITHHOLDS the summary and the prop
rows while keeping the identity and the fact of the mismatch. Two digests under different algorithms
are `unverified` rather than `hash-mismatch`: they are incomparable, not unequal, and withholding a
good description on the strength of a comparison never made would be the wrong error.

**Prop validation from a card is the half that is not cosmetic.** A foreign host can now say a
`Custom` node is MALFORMED, where before it could only fail to render it — and it reaches the same
verdict, in the same words, as a host holding the contract.

**Stylesheet.** `.fuaran-custom-summary` and `.fuaran-custom-defects` join the vocabulary; the
existing `.fuaran-custom-label` / `.fuaran-custom-props` carry the rest, which also brings the F# and
TypeScript server placeholders onto the same class set. `Theme.vocabularyFingerprint` and the four
tier copies were regenerated with `-- Css`.

**Wire and corpus.** `WIRE_FORMAT.md` §25 states the artefact, the decode rules, the obligation and
the transport convention; §11.0 gains a contract-card adoption table beside the render-obligation
one. The corpus grows its own `cards/` family with the `contract-card-round-trip` /
`contract-card-reject` kinds — **its own family and never `nodes/`**, because a card is not a node
and the node round-trip law says nothing about it. `manifest.json` is the authoritative enumeration.

## Recorded breaking change — 0.48.0, the typed-`Actor` DAG fold (fuaran#1144)

**What it is.** `DagOpRecord` carried a bare `UserId: string` where the linear `OpRecord` has carried
the typed `Actor` (`Human of id` | `Agent of model * version * id`) since Phase 320. It now carries
the same `Actor`, encoded by the same pinned `Actor.encode`. Three surfaces move together:

```text
// content-address pre-image (0.47.0)
{"parents":[…],"op":…,"ts":1700000000,"userId":"u1","promptId":…,"result":…}
// content-address pre-image (0.48.0)
{"parents":[…],"op":…,"ts":1700000000,"actor":{"kind":"human","id":"u1"},"promptId":…,"result":…}

// canonical wire record (0.47.0) — Ordinal key order put userId LAST
{"hash":…,"op":…,…,"tombstoned":false,"userId":"u1"}
// canonical wire record (0.48.0) — "actor" sorts FIRST
{"actor":{"kind":"human","id":"u1"},"hash":…,"op":…,…,"tombstoned":false}
```

**Why it is major and not a tidy-up.** Phase 408 folded attribution INTO the DAG content address to
close the same provenance hole the linear chain closed in 406/411. Typing it therefore does not merely
change a field's type — **it re-addresses every DAG node**. Every hash in `wire-format-fixtures/dag/`
was re-minted in this change; a pre-0.48.0 record's stored `Hash` will not reproduce under
`DagOpRecord.recomputeHash`, and a pre-0.48.0 hash is not a valid parent link for a 0.48.0 node. This
is the event [`Fuaran.UI.OpStream.Dag.*`](#fuaranuiopstreamdag-opt-in-rung-4--phase-178) above names
as "breakage of the `DagOpRecord` wire shape … a major-version bump on the same axis as the linear
wire format". Pre-1.0, that axis still rides a minor bump.

**Its own version slot, deliberately.** 0.47.0 was in flight and unreleased (newest tag `v0.46.0`), so
this could have ridden it on the precedent the 0.47.0 entries themselves invoke. It does not: that
slot carries four additive changes, and a wire break folded into a minor that otherwise reads as
growth is a break nobody finds when they go looking for it.

**What breaks, and what does not.**

- **Every construction site and every exhaustive read of `DagOpRecord`.** `UserId = "u1"` becomes
  `Actor = Actor.Human "u1"`; `DagOpRecord.create` / `createMerge` / `computeHash` /
  `computeMergeHash` and `GuestFork.genesis` / `step` take an `Actor` where they took a `string`. A
  host still threading a bare id lifts it with `Actor.ofLegacyString`.
- **`DagGraphNode.UserId` becomes `Actor`** in `Fuaran.UI.OpStream.Dag.Inspect`. Keeping a bare string
  on the display projection would have re-introduced, one layer up, exactly the lossy flattening this
  change removes.
- **`DagWire.decodeRecord` REFUSES a pre-1144 `userId` envelope by name** rather than lifting it. A
  lenient lift would mint a record whose stored `hash` no host can reproduce — a silent verification
  failure downstream instead of a clear refusal at the boundary. `@fuaran-ui/ops` refuses identically.
- **`SqliteDagSink` reuses the `user_id` column**, now holding `Actor.encode` JSON, exactly as the
  linear `SqliteSink` has since Phase 320. A pre-1144 row holds a bare id and reads back through
  `Actor.ofLegacyString`, so **an existing database still opens** — but its records' content addresses
  are not valid under the new pre-image, and no read path can make them so.
- **Not touched:** the linear `OpRecord` chain, its `chainFormatVersion` (`2`), `StreamEntry.encode`,
  the Node/TreeOp corpus, and every non-DAG fixture family. The DAG remains opt-in rung-4, so a
  consumer that references none of the `Fuaran.UI.OpStream.Dag.*` packages is unaffected.

**What is gained, stated plainly.** Attribution was already hashed but as an untyped string, so a
`Human "planner"` and an `Agent("claude", "4.8", "planner")` produced the *same* DAG content address —
the AI-accountability distinction the linear chain has protected since Phase 320 was flattened away
at the DAG boundary, and `DagOpRecord.ofLinear` discarded it on the way in. Both now hash distinctly,
and the degenerate embedding of a linear history into the DAG is attribution-faithful.

**Migration.** [`docs/migrations/1144-typed-actor-dag-fold.md`](docs/migrations/1144-typed-actor-dag-fold.md).
There is no in-place upgrade for a persisted DAG: **old DAG addresses do not carry forward.**

**The DAG pre-image still carries no format-version tag.** The linear chain's `"v"` has no DAG
counterpart (see the linear entry above: "a DAG version tag is tracked separately"). Introducing one
is a separate design act with its own cross-host concept; it was deliberately not bundled here.

## Recorded change — 0.48.0, FUARAN114 grid field grounding (fuaran#1149)

**One new DU case, no new dependency, no wire change.**

```fsharp
type PreEmitDefect =
    | …
    | GridFieldUngrounded of nodeId: string * field: string * schemaColumns: string list
```

**What it does.** A `DataGrid` names a column its own source cannot produce — a column `field`, or
the grid's `rowKeyField`, absent from the statically-known schema of the `Binding.Transform` the grid
reads. The row projection resolves the name against each row and finds nothing, so the cell renders
blank; for `rowKeyField` every row keys off the same empty string and row identity silently
collapses. A blank cell is indistinguishable from a legitimately empty value, which is what makes
this worth a code rather than a look at the screen.

**It is the read-side twin of FUARAN086**, which grounds a chart's field references against the same
schema over the same window, and it is deliberately the same restraint: the window is a `Transform`
over an `Embedded` table with an EMPTY pipeline. A non-empty pipeline changes the column set (derive
adds, project/groupBy remove), and a `Ref`, `Query`, `State` or host `Static` row source is unknowable
before the tree runs — all pass ungrounded, per the fuaran-core#90 rule that a validator refuses only
what is provably wrong. Grounding is *narrower* than coverage on purpose: an Error that is
occasionally wrong gets suppressed, and a suppressed rule protects nothing.

**Where it lives, and the alternative that was declined.** In `PreEmitValidate`, beside the chart
rule it mirrors. Hosting it instead in the tier where a pipeline's output schema is already
computable was considered and rejected: it would put one rule in two homes, with two vocabularies and
a code space that no longer says where a defect came from. This change introduces no package
reference — `Fuaran.UI` has pinned `Fuaran.Core.DataFrame` since the compute layer landed, and takes
no dependency on the bounded-program tier.

**The widening is a pin move, not a redesign.** `fuaran-core#112` shipped a pipeline output-schema
walk into the dataframe package this file already consumes; widening FUARAN114 past the empty-pipeline
window is one call at the one call site, once the pin here can name the version carrying it. The
`Fuaran.Core.*` pins in this repo are deliberately held behind what the public index serves, and this
rule does not wait on that to be useful.

**Breaking-change classification: a new DU case, which breaks exhaustive matches over
`PreEmitDefect`.** In-repo the only exhaustive match is `PreEmitValidate.describe`, which gains the
arm. It rides the in-flight **0.48.0** on the same reasoning as the entries above.

**The corpus moves with it (§11 forward-coupling).** `validator/defect-vocabulary.json` gains the
FUARAN114 entry — generated by reflection over the defect DU, so it needs no hand edit, only a
regeneration — and this host's `validator-coverage.json` gains the code, its `reference` posture
meaning it implements every code in the vocabulary. **The regeneration is not in this change-set:**
the corpus is a separate repository and a concurrent session owns its regeneration this wave, so the
two land separately and the coverage declaration here is the half that could move now.
