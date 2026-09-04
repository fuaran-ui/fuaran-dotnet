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
- **The document shell's language declaration — CHANGED in Phase 1114, and behaviourally breaking
  for one case.** `DocumentShell` gains a `Locale: LocaleSource` field (a recompile for any host
  constructing the record literally rather than through `DocumentShell.create`), and `lang` + `dir`
  on `<html>` are DERIVED from it — `LocaleSource.Explicit "ar-EG"` emits `lang="ar-EG" dir="rtl"`,
  and `LocaleSource.Ambient` resolves against the host's own
  `FuaranGiraffeOptions.Sources.Locale` through the new `Document.renderWithLocale`. **The
  hardcoded `lang="en"` default is gone**: a shell that declares no locale now emits neither
  attribute. That is the breaking half, and it is deliberate — the previous default asserted English
  about every document any host ever rendered, including the right-to-left ones this phase exists to
  make renderable, and a wrong `lang` is worse for a screen reader than an absent one. Adopting is
  one call: `DocumentShell.create title |> DocumentShell.withLocale "en"`. A host-authored `lang` /
  `dir` in `HtmlAttributes` still wins and is emitted once. The ambient locale also joined the ETag's
  options signature, so it is a render-cache key — two option sets differing only in locale no longer
  share an entry.
- **`dir="auto"` on runtime-bound display leaves — ADDITIVE, Phase 1114, but it changes rendered
  markup.** Both renderer arms now emit `dir="auto"` on the wrapper of a display-leaf node whose
  visible text comes from a `TextSource.Bound`, so the browser resolves that element's direction from
  its own content. The policy is the shared, kind-level, total `Accessibility.isBidiIsolated` /
  `bidiAttributes`; it is parity-locked across SSR and CSR by construction (same function, same
  position in the attribute list). A consumer asserting exact wrapper markup for such a node sees one
  new attribute. `Literal` and `I18n` text is untouched, and no container ever carries it — see
  [`docs/DECISIONS.md`](docs/DECISIONS.md) D6 for what decides the slot set.
- **The reference stylesheet's inline-axis rules are LOGICAL as of Phase 1114 — behavioural, and it
  propagates to every tier copy.** `margin-inline-start` / `padding-inline-end` /
  `border-inline-start` / `inset-inline-*` / `text-align: start|end` / `float: inline-end` replace
  their physical forms, so the sheet mirrors under `dir="rtl"` with no override block. Under `ltr`
  the rendering is unchanged; under `rtl` it is the point. Three physical usages survive with reasons
  recorded in the sheet's own header, and the only `[dir="rtl"]` rules in the sheet mirror the
  disclosure chevron's GLYPH, never geometry. Host tiers ship a byte-copy and inherit this via
  `-- Css`; a host that re-implemented the class hooks against
  [`docs/HOST-STYLING-CHECKLIST.md`](docs/HOST-STYLING-CHECKLIST.md) must make the same substitution
  itself.
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

> **Closed in 0.74.0 (fuaran#1152)** — by a different mechanism than the one measured here. See the
> entry at the end of this document; the measurement above is what ruled the hand-authored
> `[<Obsolete>]` out, and it still does.

## Recorded change — 0.50.0, the node-level `Tooltip` trait (fuaran#1112)

**BREAKING for record-literal construction of `Node`, additive on the wire.** `Node<'Msg>` gains
`Tooltip: TextSource option`, beside `Accessibility` / `Motion` / `State` / `Style`. Two new pre-emit
defect codes. No existing document encodes or decodes to different bytes: the field omits at `None`,
which every node that does not declare a hint has.

**Why this is a MINOR bump and not the same unreleased 0.48.0 the two phases before it landed in.**
0.48.0 is untagged and exists on no registry, so the producer-side rule — *a public-contract change
must not ship under a version somebody could already have restored* — would permit it, and that is
the precedent fuaran#1110 and #1111 each cited. It does not transfer here, for a reason about what a
version NUMBER says rather than about what it can serve. Both 0.48.0 entries open with *"Additive. No
shipped surface changes shape"*, and this change does change a shipped record's shape: adding a field
to `Node` is `FS0764` — an ERROR, not `FS0025`'s warning — at every full-literal construction site.
Landing them under one number would make that number say something false about what adopting it
costs, and a consumer reading a changelog is the person the number is for. So the class of the change
moves the version, and the restorability of the old one does not save it.

**What actually breaks, and what does not.** A consumer that constructs a `Node` with a FULL record
literal (`{ Id = …; Kind = …; Accessibility = …; ExtraAttributes = …; Motion = …; State = …;
Style = … }`) stops compiling until it adds `Tooltip = None`. Everything else is untouched: the
`Fuaran.*` smart constructors fill the field, copy-and-update (`{ node with … }`) is unaffected, and
`Fuaran.UI.CSharp`'s factories are the veneer's only construction path. The estate's own count when
this landed was 42 generated constructors plus one hand-written projection `mk` and roughly fifty
literals across tests and samples — every one of them named by the compiler, in one build.

**One of those fifty was a finding worth recording**, because it is the shape that will recur. A
PROJECTED kind (`Fuaran-Core`'s `Gen.KindProjection`, Phase 945) supplies its own `mk` as a
hand-written string, so the node envelope in it is NOT derived from `Idl.NodeFields` the way the
generated constructors' is. `mkSwitch` was therefore the single constructor out of forty-two that the
regeneration did not update, and it surfaced as one `FS0764` on a generated file. The coupling is
left as it is deliberately — the compiler names the one site in the same build that adds the field,
which is a stronger guarantee than a convention — with a note added beside the literal so the next
envelope field is expected there rather than discovered there.

**Surface added.**

- `Node<'Msg>` gains `Tooltip: TextSource option`; wire key `tooltip`, last in the envelope's ordinal
  key order, omitted when `None`. `TextSource` and not `Binding<string>` because a hint is CONTENT —
  authored, translated, and covered for the runtime case by `TextSource.Bound`.
- `Fuaran.Node.withTooltip` (F#) and `FuaranNode.WithTooltip` (C#) attach it. Deliberately a
  decoration on a built node rather than an option on each of the forty-one factories: the trait is
  uniform across kinds, so a per-kind option would be forty-one spellings of one thing.
- The VB XML dialect admits `tooltip` on **every** element, applied at `FuaranXml.Translate`'s single
  choke point. The analyzer's vocabulary gains a `Universal` attribute set for the same reason, and
  the authoring-surface pin now DERIVES the node-envelope half of its allow-list from the IDL's own
  `nodeFields` rather than a hand-kept list — so the next attribute-eligible envelope field is
  admitted by the act that declares it, while a structured one is still caught.
- `Fuaran.UI.PreEmitValidate` gains **FUARAN118 (Warning)** — a declared hint that resolves to an
  empty or whitespace `Literal`, FUARAN111's argument at the trait next door — and **FUARAN119
  (Warning)** — a hint on a node declaring `accessibility.hidden = Static true`, where
  `aria-hidden` takes the hint and its `aria-describedby` out of the accessibility tree with it. Both
  judge only the statically-decidable case, on the family's standing restraint.
- Both renderers emit the hint as `<span class="fuaran-tooltip" role="tooltip" id="{nodeId}-tooltip">`,
  the LAST CHILD of the node's wrapper — which is what makes it hoverable and persistent (WCAG
  1.4.13) rather than merely styled that way: the pointer travelling from the node onto the hint never
  leaves the `:hover` that revealed it.
- **The element that carries `aria-describedby` is the element that takes focus.** Where the a11y
  projection forwards to a natively-focusable semantic element (`Button`, `Link`, `Media`, `Embed`)
  the description rides that element; everywhere else it rides the wrapper and the wrapper takes
  `tabindex="0"`. `Image` forwards and `<img>` is not focusable, so it takes the wrapper pair — the
  case that shows the rule is not simply `forwardsToSemanticElement`. An existing
  `accessibility.describedBy` is MERGED into the id list, never replaced.
- An EMPTY resolved hint emits nothing at all — no element, no `fuaran-has-tooltip` class, no
  `aria-describedby`. Advertising a description that is not there is worse than silence.
- The reference stylesheet gains `.fuaran-has-tooltip` and `.fuaran-tooltip`; the emitted class
  vocabulary moved, so `Theme.vocabularyFingerprint` is restamped. The reveal is pure CSS
  (`:hover` / `:focus-within`), which is the whole affordance under SSR, and the transition is
  suppressed under `prefers-reduced-motion`.
- `Fuaran.UI.OpStream.Dag.Merge` gains a **`tooltip` merge facet**. Not bookkeeping: without a probe
  of its own a tooltip-only edit varies the KIND facet's canonical bytes and is reported as a
  concurrent edit to the node's kind, and without a pick of its own the rebuild takes the base node's
  hint and discards an uncontested edit on either side in silence.
- **No `TreeOp`.** `Accessibility` has none either, and for the same reason: the node-level traits are
  reached by `EditNode` / `ReplaceNode`, and `UpdateProp` is spec-scoped — which also means the
  superseded `Button.Tooltip` slot keeps its `UpdateProp "Tooltip"` route with no ambiguity introduced.

**`ButtonSpec.Tooltip` is SUPERSEDED and kept compiling.** It is host-only typed surface (§10.1:
never emitted, restored to `None` on decode), so no decoded tree on any host has ever carried one. It
is not removed: it is a shipped public field that renders correctly for the in-process authoring path
it was built for, and deleting it would break that path's consumers to buy nothing the node-level
trait does not already give a new one. Documented as the non-wire legacy spelling in `Types.fs`.

**Version.** Minor on the producing packages — **0.50.0**. Advanced from 0.48.0 for the reason
above, and then from 0.49.0 for a second one: fuaran#1154 cut 0.49.0 while this phase was in the
gate, and landed first. Its number stands and this one moves, on the reasoning fuaran#1111 recorded
when the same thing happened to a defect code — a released identity outlives the phase that minted
it, so two changes sharing one is worse than either moving. The argument is sharper here than it was
there: 0.49.0 is an ADDITIVE authoring-surface change, and a consumer adopting it must not discover
a source-breaking record widening inside it. Still untagged; `v0.46.0` is the newest tag on this
family, so nothing here is a re-release.

## Recorded change — 0.48.0, `NodeKind.Embed` — the sandboxed third-party embed (fuaran#1111)

**Additive. One new `NodeKind` case, one new spec record, one new enum, one new egress class, two
new pre-emit defect codes, two new render-obligation claims.** No shipped surface changes shape, and
no existing document encodes or decodes to different bytes.

**Adding a `NodeKind` DU case is MINOR, not major**, per the semver section above: an existing
consumer keeps compiling and gains an `FS0025` exhaustiveness warning, which is the correct signal.
The same reading covers `Sanitize.EgressClass`, which gains an `Embed` case for the same reason and
carries the same consequence — a host matching exhaustively over the class set is warned, and a host
that composes a policy with `Sanitize.allowOrigin` is unaffected, because an undeclared class is
denied and denial is what an embed already got before this phase existed.

**Why a KIND and not a `Mount` variant, a `Media` variant or a composition.** `docs/VOCABULARY.md`
Appendix A carried this row as "Covered by `Mount`", and the coverage claim was false. `Mount`
composes a COOPERATING guest — a scope id, a declared message channel, a capability request list, a
host-side loader that produced the guest tree — and a third-party page has none of those and cannot
acquire them; widening `Mount` to admit an uncooperative third party would weaken every guarantee
`Mount` makes. It is equally not a `Media` variant: `Media` fetches an asset and DISPLAYS it, where
this fetches a document and lets it EXECUTE, which is a different threat class and gets a different
egress class. The Appendix A row is amended from "Covered" to ADMITTED in this same change-set, per
the rule the Phase 1076 admission established.

**Surface added.**

- `Fuaran.UI.Types` re-exports `EmbedSpec` + `EmbedPermission`; `NodeKind` gains `Embed of
  EmbedSpec`. `EmbedSpec` is `{ AspectRatio: ImageAspect; Permissions: EmbedPermission list; Src:
  Binding<string>; Title: TextSource }`. `Src` and `Title` are REQUIRED; `AspectRatio` omits at
  `Natural` and `Permissions` at the EMPTY list.
- **`AspectRatio` REUSES `ImageAspect`** rather than minting a parallel enum with identical cases.
  The cases are pure layout ratios with nothing image-specific in them, the wire carries bare
  strings so the type name reaches no document, and two closed sets that must be kept in step is the
  defect a rule-of-three would be guarding against — not the reuse. It is omit-at-`Natural` rather
  than an option because an option over an enum that already contains `Natural` would give one fact
  two spellings.
- `EmbedPermission` is `AllowScripts | AllowSameOrigin | AllowForms | AllowFullscreen`, closed. The
  EMPTY list is TOTAL DENIAL, so the wire-cheapest document is also the most locked-down one. The
  exclusions are the design: there is no top-level-navigation case (the drive-by redirect) and no
  downloads case, and neither is reserved; popups, modals, pointer lock, presentation and
  orientation lock have no recorded demand and are reserved as names a later addition would take.
- `Sanitize` gains `EgressClass.Embed` (wire name `embed`), `sanitizeEmbedSrc` and
  `sanitizeEmbedSrcForEgress`. The `embed` scheme floor admits **`https` and nothing else** — not
  `http`, and not a schemeless reference either, because a relative reference names a same-origin
  document, which is exactly where `AllowSameOrigin` plus `AllowScripts` lets the framed document
  reach its own frame element and remove the sandbox. One accepted scheme and no positional test, so
  the class cannot inherit §19 rule 5's evasion surface.
- `Fuaran.UI.PreEmitValidate` gains **FUARAN115 (Error)** — an `Embed` whose `Title` is an empty or
  whitespace `Literal`, FUARAN108's argument one kind over — and **FUARAN116 (Warning)** — an embed
  declaring both `AllowScripts` and `AllowSameOrigin`, the documented sandbox escape on a
  same-origin frame. FUARAN116 is a Warning and not an Error deliberately: the pair is also what
  every real cross-origin embed needs, nothing in the tree says which case this is, and a rule that
  refuses the ordinary case is one authors switch off.
- `Fuaran.UI.RenderFidelity` gains two obligation claims — `sandbox-always-exactly-declared` and
  `refused-embed-source-omitted` — and the `Embed` row carries them plus the reused
  `accessible-name-always`. **A new claim is a new obligation for every host in the §11.0 roster**:
  hosts that have not adopted report them unchecked, which is the artefact working as designed.
- The renderers emit `<iframe class="fuaran-embed">` with `sandbox` ALWAYS present (empty when
  nothing is granted), the declared tokens de-duplicated and in declaration order, `allow="fullscreen"`
  only where declared, an always-emitted `title`, unconditional `loading="lazy"` and
  `referrerpolicy="strict-origin-when-cross-origin"`, and NO `src` attribute at all when the embed
  egress class refuses the source — the refusal recorded as a data attribute instead.
- The reference stylesheet gains `.fuaran-embed` and the `.fuaran-embed-aspect-*` family; the
  emitted class vocabulary moved, so `Theme.vocabularyFingerprint` is restamped.
- The C# veneer gains `EmbedOptions` + `EmbedPermission` and a `Fuaran.Embed` factory; the VB XML
  veneer gains an `<Embed>` element with `src` / `title` / `aspect-ratio` / `permissions`
  (pipe-separated, the `<Mount>` `capabilities` shape), both joining the analyzer's vocabulary.

**Version.** Minor on the producing packages — 0.48.0, already the working version and still
untagged, so this kind lands in the same unreleased minor as fuaran#1110. That is the precedent this
repo set one phase ago rather than a shortcut: the producer-side rule the workspace mandate states
is that a public-contract change must not ship under a version somebody could already have restored,
and no tag names 0.48.0 (`v0.46.0` is the newest on this family).

## Recorded change — 0.48.0, `Media` text tracks + transcript (fuaran#1110)

**Additive. Two new spec-record fields, one new record, one new enum, one new pre-emit defect code,
three new render-obligation claims.** No breaking change to a shipped surface, and a document that
carries neither field encodes and decodes to exactly the bytes it did before: `tracks` omits at the
EMPTY LIST and `transcript` is an ordinary optional, so `Defaults.media` still encodes to the four
keys it always did.

**Why the field tier and not a variant or a kind.** `docs/VOCABULARY.md` §2.1: the consumer has
already chosen `Media` and already chosen `Video` or `Audio`, so a track list is a REFINEMENT of a
control that is otherwise unchanged. The Appendix A `Media` row is amended in the same change-set
(the rule the Phase 1076 admission established), and the amendment is what makes that row's
irreducibility claim — "captioning a11y no existing kind expresses" — true rather than asserted.
The tier's one disadvantage is acknowledged there: a field addition skips §11.2 vocabulary
attestation, so only the fixtures catch a lagging host.

**Surface added.**

- `Fuaran.UI.Types` re-exports `TrackEntry` + `TrackKind`; `MediaSpec` gains `Tracks: TrackEntry
  list` and `Transcript: TextSource option`. `Defaults.media` carries `[]` and `None` — `Defaults`
  never invents content, and neither slot has a value that could be invented.
- `TrackEntry` is `{ Default: bool; Kind: TrackKind; Label: TextSource; Src: Binding<string>;
  SrcLang: string }`. Four of five members are REQUIRED, which makes it the strictest record on the
  wire. `SrcLang` is required for EVERY kind where HTML asks for it only on subtitles: it is what
  orders a track menu, drives pronunciation, and tells two same-labelled tracks apart.
- `TrackKind` is `Subtitles | Captions | Descriptions | Chapters`, closed. There is deliberately no
  `Metadata` case — its cues are rendered by no user agent and read only by script, so a
  declarative document naming it would state an intent no host can honour.
- `Fuaran.UI.PreEmitValidate` gains **FUARAN113 (Error)** — a track whose `Label` is an empty or
  whitespace `Literal`. FUARAN108's argument one level down: a track's label IS its entry in the
  user agent's menu, so an unlabelled one leaves a reader choosing between two identical entries.
  Only a literal is judged, the same restraint FUARAN108 shows. The rule deliberately does NOT
  require a captions track on a `Video`, and the defect's own doc comment records why: nothing in
  the wire distinguishes a lecture recording from a decorative silent loop, and a rule that fires
  on the second is one authors switch off — which costs the estate the rule on the first.
- `Fuaran.UI.RenderFidelity` gains three obligation claims — `authored-child-order`,
  `single-default-per-kind`, `transcript-disclosure-named` — and the `Media` row carries them plus
  a widened `refused-source-dropped` covering a refused track source. **A new claim is a new
  obligation for every host in the §11.0 roster**: hosts that have not adopted report them
  unchecked, which is the artefact working as designed rather than a regression.
- The renderers emit `<track kind srclang label default>` children in AUTHORED order (the opposite
  of `srcSet`, and §3.6.6 says why), honour the first `default` election per kind, drop a track
  whose source the egress floor refuses, and render a declared transcript as a `<details>`
  disclosure BESIDE the transport carrying the media's label as its accessible name.
- The reference stylesheet gains `.fuaran-media-group` and the `.fuaran-media-transcript*` family.
- The C# veneer gains `TrackEntry` + `TrackKind` and `Tracks` / `Transcript` on both
  `VideoOptions` and `AudioOptions`; the VB XML veneer gains a `<Track>` child element and a
  `transcript` attribute, both joining the analyzer's vocabulary.

**Version.** Minor on the producing packages — 0.48.0, already the working version.

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

## Recorded change — 0.49.0, the VB state-binding spelling (fuaran#1154)

**Two additive surfaces, one new analyzer code, and no byte any shipped tree encodes to
changes.**

**1. `Fuaran.UI.VisualBasic` — a second binding prefix in the XML dialect.** An attribute value
of `"$state.<key>"` now translates to `Binding.State(key, None)`; `"$name"` keeps its meaning as
`Binding.Query` exactly as before. Every reader in `Attributes.vb` learns it — the five scalar
readers and the three sequence readers — so the spelling reaches every slot the dialect can bind,
not only the ones this phase's tests exercise.

**Why the dialect needed it at all.** A query binding is host-fed and read-only, so a control whose
value binding is a query has nowhere for the control write-back default (0.x, fuaran#426) to write.
Until this change a VB author could bind an interactive control only to a query, which is precisely
the `FUARAN069` inert-control shape — so a *writable* control was authorable from VB only by dropping
to the C# factory surface. The Phase 577 VB sample said so in a comment; it now authors the control
instead.

**No default is invented, and that is a wire decision rather than a taste one.** A `State` binding
with a declared default encodes a `defaultValue` member and one without omits it — two different
documents, both in the corpus (`controls-declarative.json` carries each). Choosing a default here
would have made the undefaulted form unspellable, and the module already states the rule for itself
twice (`OptEnum`, `OptBoolAttr`): absence is not a default. The initial value has a home where it
matters — `default-open` on a `Disclosure`, a field's own auto-bind — and the defaulted binding stays
authorable through the C# factory surface. **A defaulted state binding therefore has no XML
spelling**, deliberately; adding one is a second syntax, not a fix.

**2. `Fuaran.UI.CSharp` — `Binding.State<T>(string key)` (additive).** The no-default overload,
mirroring the F# `binding.stateNoDefault` one-for-one. The translator drives it rather than
constructing the F# DU case directly, which is what keeps the VB tier's stated invariant true (it
names no FSharp.Core type on its authoring surface). It closes the identical gap for C# authors: the
corpus's undefaulted `select` `value` was a document no C# author could express either.

**3. Two reinterpretations, both narrow, both deliberate.** `"$state"` and `"$state."` were queries
named `state` and `state.`; they are now translation errors, because the prefix must carry a key.
`"$stateful"` is untouched and remains a query named `stateful` — the discriminator is the `$state.`
prefix, not the letters. No shipped sample or fixture used either affected spelling.

**4. `FUARAN117` (Error) — a `"$state"` value carrying no key.** Allocated at the top of the band
rather than beside the analyzer's own FUARAN060/061, which already collide with the F# validator's
codes of those numbers (`docs/VALIDATOR-MANIFEST.md` FUARAN060 is the extra-attribute allowlist;
FUARAN061 the blank-code check). That collision is pre-existing and is not repaired here; minting a
third number into the same range would have deepened it. FUARAN117 is the next free number — 115 and
116 were claimed by the embed rules while this phase was in flight, which is the ordinary hazard of
allocating at the top of a shared band and is why the number is asserted by the analyzer's own tests
rather than by a comment.

**The analyzer half is not a follow-up, it is the same change-set.** `FUARAN010` checks every
`$`-prefixed value against the manifest's query list, and a state key is not a query name — so a
translator that learned `$state.` without the analyzer learning it would report every valid state
binding as an unresolved query. The analyzer test proves this directly: with the discriminator
disabled, `open="$state.panelOpen"` raises FUARAN010.

**The analyzer's copy of the spelling is pinned to the translator's.** A netstandard2.0 analyzer
cannot load the net10 veneer, so the duplication is structural; `FuaranXml.IsStateBinding` /
`StateBindingKey` are public for the same reason `KnownElements` is, and a case table in the analyzer
test asserts the two agree — the `Vocabulary.Kinds == KnownElements` discipline applied one level
down.

**Classification: minor on the pre-1.0 axis.** Additive on both public surfaces; no encoder, decoder,
spec record or fixture changes, and no wire vocabulary is added — a `State` binding is a shape the
wire has always carried, and this phase adds a *spelling* for it in one authoring dialect. It takes
its own version slot rather than riding the in-flight 0.48.0, whose headline is a breaking DAG
re-address.

## Recorded change — 0.51.0, `FormFieldKind.Combobox` (fuaran#1113)

**Additive on every axis.** `FormFieldKind<'Msg>` gains `Combobox of allowFreeText: bool * onChange:
(string option -> Action<'Msg>) option * options: Binding<SelectOption list> * value: Binding<string>
option` — the typeahead / autocomplete control. A DU CASE addition leaves every existing pattern
match compiling and raises `FS0025` only where a match is exhaustive, which is the correct signal;
contrast the 0.50.0 entry above, where a new FIELD on a shipped record was `FS0764` at every
full-literal site. Adding a case is the cheap direction, and this document's own §2.1 note in the
[vocabulary charter](docs/VOCABULARY.md) says why: widening an existing case would have been the
expensive one.

No shipped document's bytes move. The case is new, so nothing previously encoded it, and
`allowFreeText` omits at `false` — which makes the SHORTEST combobox document the CONSTRAINED one.
That is a deliberate default rather than a spelling economy: an emitter that says nothing gets the
shape a `Select` would have had, and admitting values outside the option set is the thing it has to
ask for.

**Surface added.**

- `FormFieldKind.Combobox`. Wire key order is the record's ordinal one — `allowFreeText`, `onChange`,
  `options`, `value` — with `options` the only required member. The value slot and the handler are
  `Choice`'s (`Binding<string>` whose absent `Static` payload is "no selection"; `string option`),
  deliberately: the constrained combobox IS a searchable select, so the two must not differ at a call
  site that migrates between them, and with free text an empty entry is genuinely no value.
- The option source is an ordinary `Binding<SelectOption list>`, so a `Query`-bound source is the
  asynchronous suggestion feed with **no coordination vocabulary minted for it**. That reuse is half
  of the irreducibility argument in the charter row, not an implementation convenience.
- `Fuaran.FormFieldKind.combobox` / `.comboboxDeclarative` and `Fuaran.FilterField.combobox` (F#),
  `Input.FormField.Combobox` / `Filter.Combobox` (C#, `allowFreeText` defaulting to `false`), and
  `kind="combobox"` with an `allowFreeText` attribute on `<Field>` and `<Filter>` in the VB XML
  dialect.
- `Fuaran.UI.Defaults.ControlValueDefaults.combobox` — a BINDING of `choice`, not a second literal:
  the searchable form of a control must move when the control moves.
- `Fuaran.UI.PreEmitValidate` gains **FUARAN120 (Warning)** — a combobox whose option source is a
  STATIC and EMPTY list. Only `Static` is judged, on the family's standing restraint: a `Query`
  suggestion feed is empty at authoring time by construction and is the shape the control exists for,
  so judging it would make the rule one authors switch off. Warning and not Error because an empty
  static list is a legitimate transitional authoring state; what is wrong is that nothing else would
  say so.
- Class vocabulary: `fuaran-combobox`, `fuaran-combobox-input`, `fuaran-combobox-list`,
  `fuaran-combobox-option`, `fuaran-combobox-option-active`, and the `fuaran-filter-combobox` chip
  class. `Theme.vocabularyFingerprint` moves with them.

**Two renderer surfaces, and they are deliberately not the same widget.** The client tier owns the
full WAI-ARIA combobox pattern — listbox popup, `role="combobox"`, `aria-expanded`,
`aria-controls`, `aria-activedescendant`, arrow / Enter / Escape / Home / End, and a pointer commit on
`mousedown` rather than `click` so the input's blur cannot close the popup first. The SSR floor is a
native `<input list>` + `<datalist>`, which IS a combobox to the user agent and needs no script at
all. **The server renderer emits no hand-written ARIA**, and that is the design: a static
`aria-expanded="false"` that can never become `true` would replace the user agent's own correct
semantics with a claim inert markup cannot keep. Nothing on the wire names a keystroke — the whole
keyboard walk is the renderer's affordance, under the affordance→op charter.

**`allowFreeText = false` is enforced server-side, and the client restore is an affordance.** Blurring
a constrained combobox with an unmatched entry restores the committed value; that is a courtesy, not a
gate. The trust boundary is `Fuaran.UI.ServerDriven.FormValidation.enforceDeclared`, which refuses a
submitted value outside a STATIC option set, and the server-driven bounds check, which refuses the
same on a filter chip's payload. Neither adds constraint vocabulary — the control's own declaration is
the constraint, read the way `RangedNumber`'s `min`/`max` already are. A non-`Static` option source is
a **recorded known limit**: that tier holds the submission and not the source it would have to check
against, and a host that can resolve it enforces it in its own `FormValidator`, which composes on top.

**Classification: minor on the pre-1.0 axis.** Additive case, additive wire, additive authoring
surfaces, one new Warning-grade defect code.

## Recorded change — 0.52.0, the fragment-expansion memo (fuaran#1151)

**Additive on every axis, and nothing on the wire moves.** A `FragmentRef` expands by deep-copying
the referenced fragment body with every interior `NodeId` rewritten under the ref's own id; that copy
was paid again on every render of every ref. This release memoises it. A memo is invisible on the
wire by construction — the expansion is a render-time projection, not an encoded shape — so no
document's bytes change, no fixture moves, and `Theme.vocabularyFingerprint` is untouched.

**`RenderContext`'s public shape is unchanged, deliberately.** The obvious home for the cache is the
per-render context, and it is the wrong one twice over: that record is built fresh at every entry
point, so a memo living there could never hit across renders, and widening a published record is a
source-breaking change (`FS0764` at every full-literal site — the 0.50.0 entry above is the worked
example). The cache is owned outside the render and borrowed ambiently instead.

**Surface added.**

- `Fuaran.UI.Renderer.FragmentExpansion` (in `Fuaran.UI.Renderer.Core`) — the memo and its lifetime
  owner. `FragmentExpansionCache(bodyCapacity, prefixCapacity)` with `Expand` / `Clear` / `Hits` /
  `Misses` / `Count` / `BodyCapacity` / `PrefixCapacity`, plus the process-global instance behind
  `expand` / `clear` / `stats` / `count` and the two `defaultBodyCapacity` / `defaultPrefixCapacity`
  bounds. `FSharp.Core` + `Fuaran.UI` only, Fable-clean, composed from the shipped
  `FragmentMemo.BoundedRefMemo` and `FragmentMemo.BoundedLru` primitives — no new cache algorithm is
  minted.
- `Render.expandFragment : string -> Node<'Msg> -> Node<'Msg>` — the named primitive the `FragmentRef`
  arm calls, and `Render.expandFragmentUncached`, the same walk with no memo. The second is public
  because the first cannot be shown correct without it: the soundness suite proves the cached and
  recomputed trees are byte-identical through the canonical encoder, and the Phase 200
  `RenderAllocation` micro-case measures the pair.

**Keying is the whole soundness argument, so it is stated here and not only in the source.** The key
is the fragment BODY INSTANCE plus the ref prefix — never the fragment NAME. A name-keyed cache is
unsound: the fragment registry is first-decl-wins *per tree*, so a differently-declared tree may bind
the same name to another body, and a name-keyed entry would serve one tree's body to another tree's
ref. The body is immutable and, with the prefix, is the whole input to the expansion, so a different
body is a different key by construction rather than by a check anyone has to remember. Identity
rather than a content digest, because digesting the body costs exactly the subtree walk the memo
exists to avoid.

**Bound and concurrency, as a consumer-visible contract.** Two LRU levels — 64 distinct bodies, 32
prefixes per body — so at most 2048 namespaced bodies are retained and the strong references held to
bodies are bounded too. On .NET every probe and store runs under a private monitor while the compute
runs outside it, so the critical section is a pointer scan rather than a subtree walk and two threads
racing one key cost one recomputation, never a corrupt store or a wrong answer. Under Fable the
guard compiles out (`lock` is not portable there; the browser is one event loop). **That `#if` is the
entire divergence between the pipelines and it is not on the result path**, which is what makes the
byte-identity proven on .NET carry to the browser.

**Not a behaviour change a consumer can observe**, with one deliberate exception: a host that mutates
a fragment body *in place* and re-renders the same instance would now see the previous expansion.
Nothing in the language admits that — `Node` is an immutable record and every documented edit path
produces a new tree — so this is stated as the boundary of the contract, not as a known defect. A
host in doubt calls `FragmentExpansion.clear ()`.

**The server renderer is untouched.** `Fuaran.UI.Renderer.Server` renders a referenced body **without
namespacing interior ids**, so it has no expansion to memoise. No SSR parity claim moves.

**Classification: minor on the pre-1.0 axis.** New module, two new functions, no existing surface
changed, no wire event.
## Recorded change — 0.53.0, `read.nodeJson` on the DevTools relay (fuaran#742)

**Additive, and outside the wire format entirely.** Nothing in `Fuaran.UI`'s typed tree, its
canonical encoding, or any shipped document moves. What changes is the *relay* — the page↔extension
`postMessage` contract specified in `DEVTOOLS_RELAY.md` and versioned independently of the wire
profile `core@1.0` — which advances from `relay@1.1` to **`relay@1.3`** with a seventh read entry
point, `read.nodeJson` (§7.7), and an eighth refusal class, `ENCODE_FAILED` (§9.3). The two
intervening minors are already-specified additions this host now serves a session at: `relay@1.2`
added only an optional `attribution` field that this peer reads for nothing.

**Why a relay bump is safe for every existing client.** §6.3's profile selection — shipped here at
0.32.0 — answers with the highest profile the CLIENT accepts and this peer can serve, and advertises
capabilities at *that* profile. So a `relay@1.0` client keeps getting a `relay@1.0` session, is not
told about an entry point its own contract does not define, and is refused `CAPABILITY_ABSENT` if it
asks for one anyway. The shared conformance corpus keeps its `relay@1.0` fixtures unchanged precisely
to hold that claim to evidence.

**Surface added.**

- `Fuaran.UI.Renderer.Relay.NodeJsonLookup` — `NodeMissing | Encoded of RelayValue | EncodeFailed`.
- `Relay.RelaySurface.NodeJson: string -> NodeJsonLookup`. **This is a record field on a shipped
  record**, so it is `FS0764` at every full-literal construction site — the 0.50.0 direction, not the
  0.51.0 one. `Relay.surfaceOf` fills it, so a host that builds its surface through that function (the
  supported route, and the only one the renderer uses) is unaffected; a test or host constructing a
  `RelaySurface` literal adds one field.
- `Relay.Profile` is `"relay@1.3"` (was `"relay@1.1"`). A literal, so a consumer that inlined it
  recompiles.
- `DebugGlobal.Version` is `"0.3.0"` (was `"0.2.0"`), and `window.__fuaran` gains
  `getNodeJson(id)`. The console global's shape is DEBUG-ONLY / UNSTABLE and excluded from semver, as
  the rest of this document records; the version is reported in `hello.ok` as `surfaceVersion` and is
  informational there.

**Why `EncodeFailed` exists when this host cannot raise it.** The canonical encoder is total over
live trees — a value the wire format cannot carry is encoded as a sentinel string, not refused — so
`surfaceOf` never returns that case. It is in the DU because a host whose local tree model is wider
than the wire vocabulary otherwise has only `NODE_NOT_FOUND` available, which is a lie about a node
that is plainly there. A closed refusal set that forces an implementation to misreport is not a
stricter contract; it is a less truthful one.

---

## Recorded change — 0.63.0, cross-container transfer — moving a row between two grids (fuaran#1123)

**Additive on the wire; SOURCE-BREAKING at one narrow place; a `WIRE_FORMAT.md` §11
forward-coupling event.** Every pre-0.63.0 document encodes and decodes to byte-identical bytes,
and every pre-0.63.0 grid renders exactly as it did — both new members are optional, absence is
their only spelling of "declares nothing", and a grid declaring neither emits the DOM it always did.

**`DataGridSpec.TransferOutKey` / `.TransferInKey` — a §2.1 FIELD PAIR, and the first wire members
in this tier whose subject is a RELATION BETWEEN TWO NODES.** Between them they say one thing: these
grids exchange rows. A grid declaring `transferOutKey` K may release rows onto the State key K; a
grid declaring `transferInKey` K accepts them from it; a grid declaring both with one K does each.

  * **On the wire:** `transferOutKey` / `transferInKey`, both omitted when absent. A present member
    of any type but string is `WRONG_TYPE` and is not coerced.
  * **The record a drop writes** is fixed normatively in `WIRE_FORMAT.md` §3.6.13:
    `{"itemId","from","to","index"}`, all four always present, `index` 0-based and written even at
    `0`. It is a STATE value, not a member of any node, so no fixture carries it — the sort
    descriptor's posture exactly.
  * **Source-breaking surface:** `DataGridSpec` is a record, so a FULL-literal construction stops
    compiling (`FS0764`). `{ Defaults.grid with … }` is unaffected, and the public `mkDataGrid` /
    `Fuaran.grid` constructors and the C#/VB facades keep their existing signatures — the pair has
    no typed-facade slot yet, which is the §11-step-6 follow-up `reorderable` already carries and
    which this release does not close.

**TWO members rather than one symmetric key**, because the one-way ends are ordinary — an archive
column that accepts and never releases, a Done column that releases nothing back — and a single key
would make those documents inexpressible. Neither takes the `-StateKey` suffix its sibling behaviour
fields carry: that suffix marks a key a grid both writes AND READS to change its own presentation,
and neither end reads this one for its own presentation.

**The renderer OWES the application, not only the gesture, and that is the release's real content.**
A drop writes the record AND commits each half through the destination that end already declares —
`editStateKey`, else the Phase-663 `State`-source floor. **No second write path is introduced**: a
transfer is a write of each end's whole rows value, exactly as an edit and a reorder are, and every
write still crosses the same tree-state-write gate, scope routing and host-reserved-key guard. An end
with no writable destination (a filtered view over one collection, which is the canonical board's own
shape) is simply not applied, and the record still names what the reader asked for. A record nothing
applies would have been a fake affordance: the reader drags, an object appears in State, and no row
moves — while `reorderable` one field over works out of the box.

**The keyboard route is part of the affordance and not a follow-up.** A drag has no keyboard analogue
and none is invented; what ships is a SECOND ROUTE — `Control+X` lifts a row on its handle, the
receiving grid's own place control puts it down, and `reorderable`'s arrow keys position it from
there. Two shipped affordances reach between them everywhere the pointer reaches. The chord is
advertised (`aria-keyshortcuts`) and every transition is announced through a `role="status"` line,
because an undiscoverable shortcut is another fake affordance.

**FUARAN129 (Warning), a transfer end with no counterpart.** Two conditions, one code, on the
FUARAN125 / FUARAN128 precedent: an accepting grid nothing releases to (a drop zone no drag can
reach) and a releasing grid nothing accepts from (a handle with nowhere to go). Judged over the whole
tree, because a per-object codec sees one grid and could never answer it — the split
`pageSize`-without-`pageStateKey` already carries. A grid's own declarations are excluded when judging
it, so a two-way column alone in a tree is reported rather than pairing with itself. Warning rather
than Error: a board whose second column is a later edit is an ordinary mid-edit state.

**FUARAN130 (Warning), a transfer end that cannot say what moved.** A grid declaring either member
with no `rowKeyField`: the record identifies the moved row, and the closure form `rowKey` crosses the
wire as `"<closure>"`, so a decoded transfer out of such a grid would report that nothing moved.
Strictly narrower than FUARAN078, which fires only when a grid has NEITHER spelling.

**The vocabulary charter's Interaction / affordance cluster is amended in this same change-set**, per
the rule the 0.46.0 `Media` admission established, and the amendment is to its GOVERNING SENTENCE
rather than to a row: that sentence named *the node that both hosts the gesture and consumes its
effect*, singular, and a transfer has two nodes of which neither alone consumes it. **The Appendix A
`KanbanBoard` row is amended in the same change-set too** — its **Composition** disposition stands and
is now honest, where until this release it redirected to a composition that could not be composed.

**`Theme.vocabularyFingerprint` → `fv1:e697c1d1c162b9a7`**; four classes entered the vocabulary
(`fuaran-grid-transfer`, `fuaran-drag-over`, `fuaran-drag-place`, `fuaran-drag-status`) and the
reference sheet gained the block that styles them. The four tier copies were regenerated in the same
change-set.

---

## Recorded change — 0.62.0, switch transitions + timed advance — the carousel behaviour (fuaran#1122)

**Additive on the wire; SOURCE-BREAKING at two narrow places; a `WIRE_FORMAT.md` §11
forward-coupling event for one of the three pieces and for NONE of the other two.** Every
pre-0.62.0 document encodes and decodes to byte-identical bytes, and every pre-0.62.0 switch
renders exactly as it did — the new wire member is optional and absence is its only spelling of
"does not advance".

**Three pieces, and they sit at three different cost tiers. The spread is the point of this
entry**, because costing all three as wire changes is the mistake this record exists to prevent.

**(1) `Motion` gains `CrossFade` and `SlideBetween` — at ZERO wire cost.** `Node.motion` is
**host-only** (`WIRE_FORMAT.md` §9: motion is consumer-authored, not AI-authored), so the enum
never reaches the wire at all: no encoder, no decoder, no schema entry, no corpus fixture, and no
obligation on any other host in the §11.0 roster. What the two cases DO cost is the reference
stylesheet (two `@keyframes` families keyed on the stage's child, not on the node), the extended
`prefers-reduced-motion` rule, `Theme.motionVar`, and the class-vocabulary fingerprint. They are
the first two tokens in the vocabulary whose subject is a TRANSITION BETWEEN renderings rather than
the arrival or the state of one.

  * **Source-breaking surface:** an exhaustive `match` over `Motion`. In practice that is
    `Theme.motionVar` and any consumer that enumerates the tokens; a consumer switching on a subset
    with a wildcard is unaffected.

**(2) `SwitchSpec.AutoAdvanceMs: int option` — a §2.1 FIELD, and the one piece that IS a §11
event.** It carries the single fact a host cannot recover from the tree: that this switch is meant
to move on its own, and how often. Everything else about a carousel was already composable and none
of it says a timer exists.

  * **On the wire:** `autoAdvanceMs`, omitted when `None`.
  * **Refused, never canonicalised:** a non-positive or fractional value is a decode error
    (`WRONG_TYPE` at `$.kind.autoAdvanceMs`). `0` is what an emitter reaches for to mean "off" and
    the language already has a spelling for off — an absent key — so accepting it would make two
    document shapes mean one thing. The `Masonry.cols` ruling at a second slot.
  * **Source-breaking surface:** `SwitchSpec` is a record, so a FULL-literal construction stops
    compiling (`FS0764`). `{ Defaults.switch with … }` is unaffected, and this release converted the
    five in-repo full literals to that form under the existing `SpecConstruction` guard. The public
    `mkSwitch` / `Fuaran.switch` constructors and the C# `Fuaran.Switch(SwitchOptions)` facade keep
    their arity; the C# options record gains an optional `AutoAdvanceMs`.

**(3) Swipe and the arrow keys — renderer-owned affordances, at NO vocabulary cost.** No gesture
name, threshold, event name, direction token or resume rule enters the language, per the
affordance→op charter. They write the switch's own selector key like any other control.

**Three WCAG 2.2.2 obligations are recorded NORMATIVELY rather than left per-host**, which is
unusual and deliberate. A client tier honouring the interval must pause the advance on hover, focus
and held touch; stop it **permanently** on any reader interaction, with no resume path; and never
start it under `prefers-reduced-motion: reduce`. The third is the one a stylesheet structurally
cannot deliver — a reduce rule can make the transition inert and cannot stop the content changing
under the reader — which is why it belongs to the renderer and is stated beside the field. A host
that honoured the interval and not the trio would ship an accessibility defect the document has no
way to ask it not to.

**FUARAN128 (Warning), a declared interval with nowhere to go.** Two conditions, one code, on the
FUARAN125 precedent: a switch selecting on a non-`State` binding (auto-advance moves the switch's
own key, and a `Selection`/`Filter`/`Query`-driven switch has none), and a switch carrying fewer
than two cases. Warning rather than Error — an inert declaration is not harmful and a second case
arriving in a later authoring step is an ordinary mid-edit state.

**The vocabulary charter's Appendix A `Carousel` / `Gallery` row is amended in this same
change-set**, per the rule the 0.46.0 `Media` admission established. The **Composition disposition
stands and is now evidenced**; what is corrected is the row's implied COMPLETENESS — it named a
composition that, until this release, could not actually be composed. The row now names its three
prerequisites and cites this phase.

**`Theme.vocabularyFingerprint` → `fv1:8068069268d58955`**; three classes entered the vocabulary
(`fuaran-motion-cross-fade`, `fuaran-motion-slide-between`, `fuaran-switch-stage`) and the reference
sheet gained the block that styles them. The four tier copies were regenerated in the same
change-set.

---

## Recorded change — 0.61.0, `NodeKind.Tree` — recursive disclosure with tree semantics (fuaran#1120)

**Additive on the wire; SOURCE-BREAKING at every exhaustive `match` over `NodeKind`; a
`WIRE_FORMAT.md` §11 forward-coupling event.** A new `NodeKind` case is the pre-1.0 minor-bump
precedent this document already records for `FormFieldKind.DateRange`: nothing is removed and no
existing document changes meaning, but a consumer matching every kind stops compiling until it adds
an arm. Every pre-0.61.0 document encodes and decodes to byte-identical bytes.

**The vocabulary charter's Appendix A row moves from undecided to ADMITTED**, in this same
change-set, per the rule the 0.46.0 `Media` admission established. The row had stood as "Kind
(reserved) **or** Composition — first attempt is a `List` + `Disclosure` composition" for a year.

**The depth argument is STRUCK, and the admission rests on SEMANTICS.** The obvious irreducibility
claim was arbitrary depth: a fixed composition cannot express unbounded nesting. It does not hold.
Cross-family emission evidence on a probe built to force depth produced no container on either
vendor — one nested four levels and stopped, which is a finite literal a static composition
expresses. That claim is struck rather than left standing weakly, because an argument nobody
re-examines is one the next proposal inherits.

What breaks the composition is BEHAVIOUR. A `List` of `Disclosure`s is N independently focusable
containers: every row is its own tab stop, no focus walks the structure, `Left` does not close the
row you are in and move to its parent, `Home` does not reach the first row of the whole hierarchy,
and no row announces its depth or its position among its siblings. The WAI-ARIA tree pattern is ONE
composite widget with a roving tabindex, and that is not a property any arrangement of separately
focusable containers has. Evidence for the semantic half is cross-family and unambiguous: handed a
task naming one focus, `Home`/`End`, a root-to-focus path and per-row open/closed/leaf announcement,
one vendor emitted the tree roles, levels, roving tabindex and all six key bindings unprompted, and
the other abandoned recursion entirely and flattened to rows carrying level and state with a
screen-reader block. Both reached for behaviour the vocabulary could not carry.

**The `NavBar` counter-precedent is what this had to answer.** The Navigation cluster declined a nav
kind BECAUSE `AriaRole.Navigation` already carried the landmark — the fact needing to be said was an
attribute, and an attribute is expressible as data on a `Box`. No projection does the same here.
`role="tree"`, `aria-level`, `aria-expanded` and `aria-setsize` are attributes and could be carried
that way; the roving focus, the six key bindings and the expand-collapse-traverse semantics are not
attributes at all. They are a behaviour the host performs over rows it has not yet expanded — the
`SortStateKey` shape.

**Scope is NARROW, deliberately.** Static recursive items only. Finite static nesting stays with
`Disclosure`, so the kind sits BESIDE the composition rather than swallowing it: the discriminator is
*keyboard traversal over a hierarchy*, not *nesting*. A bound or lazily-fetched children source is
RESERVED and out of this cut — it needs the shared-data-source charter's machinery and its own walk.

**The surface.** `TreeSpec { ExpandedStateKey: string option; Items: TreeItem list; OnSelect: (string
-> Action<'Msg>) option; SelectionStateKey: string option }` and `TreeItem { Children: TreeItem list;
Icon: string option; Id: string; Label: TextSource }` — the wire vocabulary's **first
self-referential record**. `Items` is required; `Children` omits at the EMPTY LIST, so a leaf carries
no `children` key at all, which is most of a real hierarchy.

**Both reader-driven behaviours are named State keys, and there is no `expandable` boolean.** That is
the grid-behaviour cluster's governing ruling applied without exception: the key IS the affordance,
and a flag with no key behind it is a decorative control writing state nothing reads. There is no
per-item `expanded` flag either, because a node-local shadow copy is free to disagree with the key.
The slot shapes are fixed by the specification — `expandedStateKey` holds an ARRAY OF ROW IDS,
`selectionStateKey` a bare row-id STRING — and one shared reader in `Fuaran.UI.Renderer.Core`
implements both, so the two render legs cannot spell them differently.

**A tree naming NO expansion key renders FULLY EXPANDED and static.** This is the same reading that
lets a grid honour a declared initial order while offering no interactive sorting: an initial
presentation without a reader-driven affordance is a legitimate shape, and it is the only reading
under which such a tree shows its content at all.

**Renderers.** The client leg emits the full ARIA tree pattern — `role="tree"` / `treeitem` /
`group`, `aria-level` / `aria-setsize` / `aria-posinset`, `aria-expanded` only on rows that HAVE
children, `aria-selected` only where a selection key is named, exactly one row at `tabindex="0"`, and
Up/Down/Left/Right/Home/End with the APG's two-press `Right` (a closed parent opens and stays put).
The accessible name is STATED via `aria-label` rather than computed from contents, because a
`treeitem` owns its child group and a name derived from the subtree would read the whole branch out
as the row's own name.

**The SSR floor is normative and carries no script** (`docs/SSR.md`): the identical elements,
classes, ARIA and roving tabindex, with `aria-expanded` reflecting the statically-resolvable expanded
state. Every structural decision — which rows are open, which row is focusable, what "visible" means
— is taken in `Renderer.Core` and shared, so the legs cannot come apart. What the server leg omits
is the handlers, which it has nothing to say about. The EMAIL projection drops the tree vocabulary
altogether and renders nested lists fully expanded: a mail client runs no script, so honouring a
collapsed row would hide content behind an affordance the reader will never be given.

**A fourth recursive entry point in the decoder, bounded on its own axis.** Item nesting consumes NO
node depth — a whole hierarchy lives inside one node — and the syntactic bound is nowhere near
reached at ~2 JSON levels per row, so the `MaxDepth` figure is applied to item nesting on its own
axis, on the `TreeOp.Batch` precedent §21.5 records. The figure is REUSED rather than a sixth
protocol number minted: these frames cost what the node decoder's cost. Pinned from both sides —
`limit-tree-item-depth-at-max` and `reject-limit-tree-item-depth`.

**Two new validator codes, both Error.** **FUARAN126** — a tree repeating a `TreeItem.Id` anywhere in
the hierarchy: the id is what both State keys NAME, so a repeat makes both ambiguous, and expanding
one row would open two. Judged across every level rather than per sibling group, because the keys are
flat and carry no path. **FUARAN127** — a row whose `Label` is an empty or whitespace literal, on
FUARAN108's argument moved onto a row: a row's label is the only thing a reader walking the hierarchy
has. Both judge only LITERALS, on FUARAN108's restraint.

**Duplicate row ids are a PRE-EMIT rule and deliberately NOT a decode refusal**, on §8.1's own stated
position for node ids: duplicate detection is a whole-tree property, a decoder streaming a document
is not required to carry the id set, and there is no wire error code for it. Making the reference
host refuse one would make it stricter than the format it implements.

**`Theme.vocabularyFingerprint` moved** to `fv1:a2691fe9295530d0`: five classes entered the
vocabulary (`fuaran-kind-tree`, `fuaran-tree`, `fuaran-tree-group`, `fuaran-tree-item`,
`fuaran-tree-label`). A host pinning the old value would accept a stylesheet that knows none of them.

**Wire survivability: PARTIAL, with an alternative that is not the write-back hint.** `OnSelect`
erases like every closure, but a decoded tree does not lose selection with it — the renderer writes
`selectionStateKey` on its own account, so a document naming the key keeps a working, selectable,
keyboard-navigable tree after a round trip and only a host-side side-effect is lost.

**`UpdateProp` sets nothing on this kind, and `Introspect.availableFields` returns the empty list.**
Both State keys and the row structure are whole-node `EditNode` territory: re-pointing a live
behaviour at a different key field-by-field would leave the old slot holding the state the reader had
built up, and coercing a hierarchy out of an untyped value is where a lenient parse silently drops a
subtree.

**Authoring surfaces (§11 step 6).** F# gains `Fuaran.tree` / `Fuaran.treeSpec` / `Fuaran.treeItem`;
the C# veneer gains `Tree(TreeOptions)` with a recursive `TreeItemOptions`; the VB XML dialect gains
`<Tree>` with recursively nested `<TreeItem>` children — the dialect nests natively, so a hierarchy
is authored as a hierarchy with no parent-id convention to get wrong, and `<TreeItem>` is the only
structural element in that dialect that nests inside itself. A DISTINCT element name rather than an
overloaded `<Item>`: the analyzer attribute table is keyed by element name and is global, so giving
`<Item>` an `id`/`label`/`icon` row would make `<Item id="x">` inside a `<List>` analyzer-clean while
the translator silently ignored every attribute of it.

---

## Recorded change — 0.60.0, print break control — subtree cohesion across a page boundary (fuaran#1473)

**Additive on the wire; SOURCE-BREAKING at a full-literal `BoxSpec` / `DataGridSpec` / `GridSpecOf`
construction; a `WIRE_FORMAT.md` §11 forward-coupling event.** Nothing is removed, no existing
signature changes, and every pre-0.60.0 document encodes and decodes to byte-identical bytes.

**The four slots, all `bool`, all omitted at `false`.** `BoxSpec` gains `KeepTogether` and
`BreakBefore`; `DataGridSpec` gains `KeepRowsTogether` and `RepeatHeader`. Each is appended to its
record, so no generated constructor position moves and `mkBox` / `mkDataGrid` are unchanged. The
typed author facade `GridSpecOf` gains the grid pair as well, filled by `Defaults.grid`.

**What they say.** Each is a statement about **which subtree must stay together** when the rendering
is paged: keep this block whole, do not split this row, repeat this header. That is the one fact a
host cannot recover from a rendering — a formatter laying out pages sees boxes, and nothing carries
back that the totals block reads wrong when halved — which is the `SortStateKey` shape: a behaviour
the host performs, keyed by something only the document can name.

**No medium vocabulary entered the surface, and none is implied.** Nothing here names a page size, a
margin, a sheet number, a running header or footer, or the medium itself; the paged medium is host
chrome. Nor was a screen-only / print-only member added: medium-conditional content is `Switch` over
a host-supplied binding, and that half of the ratified charter row is Covered rather than admitted.

**Where each slot lives is an irreducibility decision, not a convenience.** The container pair is on
`Box` and on no other layout kind, because a `SplitPanel`, a `Disclosure` or a `Tabs` that must stay
whole is reachable by wrapping it in a `Box`. There is no grid-level keep-together slot for the same
reason. What no wrapper reaches is a grid's ROW boundary and its header row group — nothing outside
the grid knows where either is — which is why those two are on `DataGrid` and nowhere else. There is
likewise no break-AFTER counterpart: a break after this box is a break before the next one.

**Renderer changes, invisible to a document that declares nothing.** Both renderers append
`fuaran-break-inside-avoid` / `fuaran-break-before-page` to the container's own class, in every
`Box` arm, and `fuaran-grid-rows-together` / `fuaran-grid-repeat-header` to the grid's — on the
static table AND the SSR hydration placeholder, so a bound grid does not silently drop the
declaration. The suffix is empty when nothing is declared, so every pre-0.60.0 class string is
byte-identical. **A snapshot pinning the class list of a node that DECLARES a print break will need
updating; one for a node that declares none will not.**

**`Theme.vocabularyFingerprint` moved** to `fv1:0bb0bce8e9295ec6`: four classes entered the
vocabulary. A host pinning the old value would accept a stylesheet that knows none of them.

**The paged behaviour is CSS in a `@media print` block, with no script at any point.** A no-script
page printed straight from the browser keeps the declared blocks whole and repeats the declared
header on every sheet; `break-inside` / `break-before` / `display: table-header-group` are formatter
instructions, so there is nothing for a host to observe, count or wire. Stating the medium is what
makes the SCREEN rendering unchanged by construction rather than by the accident that two of the
three properties happen not to apply on a continuous medium. The retired `page-break-*` spellings
are emitted alongside the modern ones, because an engine that honours only the old names would
otherwise silently ignore a rule the document declared.

**One new validator code: FUARAN125 (Warning)** — a print-break declaration with nothing to act on:
`repeatHeader` on a grid that renders no header cells, and `keepTogether` on a container that
renders no subtree (an empty `Box`, or a `Separator`, whose `<hr>` takes no children whatever the
spec holds). Two conditions, one code, because they are one rule at two slots. `breakBefore` on an
empty box is deliberately NOT reported — an empty box still generates a box, so a break before it
remains live — and `keepRowsTogether` gets no companion rule at all, because whether a grid has rows
is a property of its resolved source and therefore a runtime fact.

**Authoring surfaces.** The C# veneer gains `BoxOptions.KeepTogether` / `.BreakBefore` and
`DataGridOptions.KeepRowsTogether` / `.RepeatHeader`; the VB XML dialect gains `keep-together` /
`break-before` on `<Box>` and `keep-rows-together` / `repeat-header` on `<DataGrid>`, read through
the existing `AttrBool` whose absent-is-false default already matches the wire's omit-at-false. F#
needs no new helper: the fields sit on the spec records, so record-with syntax reaches them.

**`UpdateProp` sets all four**, on `Editable`'s disposition — they are plain wire booleans with a
coercion — and `Introspect.availableFields` advertises them, so the introspection surface names no
field it cannot set.

---

## Recorded change — 0.59.0, `SemanticStyle.direction` — bidirectional isolation (fuaran#1472)

**Additive on the public API and on the wire; a `WIRE_FORMAT.md` §11 forward-coupling event.**
Nothing is removed, no signature changes, and every pre-0.59.0 document encodes and decodes to
byte-identical bytes.

**The slot.** `SemanticStyle` gains `Direction: TextDirection` — a closed three-case enum
(`Auto` | `Ltr` | `Rtl`) spelled lower-case on the wire (`auto` / `ltr` / `rtl`, the
`LiveRegionKind` posture). `Auto` is the identity and is OMITTED at it, like every other member of
that record, so a `style` object that declared nothing before declares nothing now. It reaches a
host through the two paths `SemanticStyle` already had — the node envelope's `style` and the
`UpdateStyle` op's — and the corpus pins both, on the accept side and on each refusal.

**What it says, and what it deliberately does not.** `Ltr` / `Rtl` declare that ONE authored value
reads that way whatever surrounds it; the renderers emit the matching `dir` on the element carrying
the run and the reference stylesheet isolates it (`.fuaran-dir-ltr, .fuaran-dir-rtl { unicode-bidi:
isolate }`). That is a **correctness** statement rather than a presentational one: an account or
reference number declared `Ltr` inside right-to-left prose is reordered by the bidirectional
algorithm unless the run is isolated, and the reader reads its digits back in the wrong order (WCAG
1.3.2, Meaningful Sequence). It is also a statement only the document can make — a host is handed
a string and cannot know which substring is an opaque identifier rather than prose.

**No mirroring vocabulary entered the surface.** Nothing here names a document direction, a locale,
or a layout side. Mirroring the frame is host chrome: a mirrored tree and an unmirrored one are
identical in every respect a consumer can observe, and the reader's locale is the host's fact rather
than the emitter's.

**Two additive renderer changes, both invisible to a document that declares nothing.** The class
projection appends `fuaran-dir-{direction}` AFTER the existing role and voice fragments, so every
pre-0.59.0 class string is byte-identical. And a declared direction now BEATS the `dir="auto"`
heuristic — `auto` infers from the value's own first strong character, and the declaration exists
precisely for the values that inference gets wrong. An `Auto` declaration falls through to the
heuristic unchanged. **A snapshot pinning a wrapper's class list or `dir` attribute for a node that
declares a direction will need updating; one for a node that declares none will not.**

**One new validator code: FUARAN124 (Warning)** — a declared direction on a kind that lays out no
character data and holds no children to inherit it (`Icon` / `Skeleton` / `Sparkline`), which is a
dead declaration on FUARAN123's reasoning. The set is deliberately narrow: a container passes `dir`
to its children, and a chart, a grid and a map lay out text through their own arms, so all of those
pass ungrounded rather than risk a false accusation against a correct tree.

**Authoring surfaces.** F# gains `Node.withDirection`; the C# veneer gains
`FuaranNode.WithDirection` plus a `TextDirection` facade enum; the VB XML dialect gains a universal
`direction` attribute beside `tooltip`, whose token is refused rather than coerced. These are the
first `SemanticStyle` members either veneer has ever spelled, and that is deliberate: the other five
are presentation, which a veneer can reasonably omit, and this one is not.

**The isolation is markup and CSS only** — no script participates, so it holds identically under
server-side rendering with no hydration at all.

---

## Recorded change — 0.57.0, `Sparkline` draws through the shared `Drawing` builder (fuaran#1098)

**Additive on the public API; a deliberate SSR BEHAVIOUR change; no wire change at all.**

**The API.** `Fuaran.UI.Charts` gains `tryLowerSparkline` / `lowerSparkline` (the `tryLower` / `lower`
pair's shape, applied to a `Sparkline`), plus the constants the geometry is stated in
(`sparklineViewBox`, `sparklineStrokeWidth`). Nothing is removed and nothing changes signature, so
every existing consumer compiles untouched.

**The wire is UNTOUCHED, and this is the load-bearing half.** `NodeKind.Sparkline` keeps its case, its
`SparklineSpec` keeps its single `Source` field, and the `$type` token, the schema branch, the
`manifest.kinds` entry and the IDL entry are all byte-identical. This is NOT a `WIRE_FORMAT.md` §11
forward-coupling event: no document's bytes move and no document decodes differently. What changed is
how a host DRAWS one.

**The behaviour.** Both renderers retire their hand-written polyline in favour of the lowering emitted
through the shared `DrawingSvg` builder. On the CLIENT the geometry is unchanged — same 100 × 30
canvas, same coordinates to 2 dp, same `currentColor` stroke at 1.5 — but the markup is now the shared
builder's (`<div class="fuaran-sparkline"><svg class="fuaran-drawing" …><polyline
class="fuaran-drawing-polyline" …/></svg></div>`) instead of a bespoke `<svg class="fuaran-sparkline">`
carrying a `fuaran-sparkline-line` child. **A snapshot pinning that markup, or a stylesheet targeting
`.fuaran-sparkline-line`, will need updating** — the reference stylesheet never had a rule for it, and
the hook the fidelity contract names (`.fuaran-sparkline`) is unchanged, now as the container.

On the SERVER the change is larger and is the point of the release: a resolved series previously
rendered an em-dash placeholder and the values were never read. It now renders the real geometry, so
**an SSR snapshot pinning the placeholder will go red.** An unresolved or empty series still renders
the placeholder. `render-fidelity.json` moves `Sparkline` from `"class": "clientOnly"` to
`"class": "none"` accordingly — it was the only geometry-bearing kind in the vocabulary excluded from
parity by contract, and there is no longer a client-only tier to exclude.

**One latent fault fixed in passing.** A `Binding.Static None` series resolves to the slot's default
representation, which for a list is `null` rather than empty; the retired arm would have faulted on it
(`Seq.isEmpty null`) and survived only because the authoring default is a sentinel `Query` that never
resolves. The lowering guards it at the shared seam, and an absent series now takes the same branch as
an empty one.

**Accessibility, stated because it is a deliberate omission.** A lowered sparkline carries no `<title>`
and no `<desc>`. The generated summary a lowered `Chart` gets is minted from a `ChartSpec`, and a
`Sparkline` has none — so inventing one here would be new cross-host contract text that every other
host would then have to reproduce byte-for-byte. It gains the shared geometry builder, not the chart
tier's provenance or narration.

**The contract lives in the corpus**, as `wire-format-fixtures/sparkline-lowering/` — the
`chart-lowering/` family's shape, covering the ordinary series and every degenerate one (single point,
flat, the flat-guard boundary, empty, and the non-finite sentinels).

---

## Recorded change — 0.56.0, the generated layer regenerates in-process (fuaran#1181)

**No wire change at all, and no source break.** `src/Fuaran.UI/Generated.fs` — the IDL-emitted
structural layer — is REORDERED and otherwise byte-for-byte what it was. The regenerated file has
the identical length and the identical multiset of lines, save the two recursion-group head markers
that swap (`Action` now leads the group `TextSource` used to lead, because `A` sorts before `T`).
Every type, every case, every field and every emitted byte is unchanged.

**Why it moved.** The vocabulary this layer is generated from now lives in this repo
(`src/Fuaran.UI.Idl/`) and is regenerated from its own canonical `idl.json` + `support.json` through
the packaged IDL engine, instead of being byte-copied out of a sibling checkout. A vocabulary loaded
from its artifact is CANONICALISED — the artifact's ordering contract Ordinal-sorts the top-level
collections, because the document exists to be diffed — so the emission declares its 313 types, and
`NodeKind`'s 42 cases, in Ordinal order rather than in the authored family order the hand-carried
copy preserved. This is the one-time reordering `fuaran-core#114` recorded in advance, absorbed here
in the change that takes the vocabulary home.

**What this is NOT.** It is not a wire change: the canonical encoder emits by case NAME, never by
position, so no shipped document's bytes move and no document decodes differently. It is not a
source break: the case SET is unchanged, so every exhaustive `match` still compiles, and no record
gains or loses a field (the 0.50.0 / 0.53.0 / 0.54.0 / 0.55.0 `FS0764` direction does not apply).

**What it IS, and why it takes its own version rather than repacking 0.55.0.** Reordering a
discriminated union's cases changes the compiled tag ordinal, and `Fuaran.UI.Generated` is a public
module that shipped packages consume (`Fuaran.UI.Charts`, `Fuaran.UI.Ops`,
`Fuaran.UI.OpStream.Abstractions`). Tag ordinals are binary contract — they govern structural
comparison order and anything reading `Tag` — and a contract change never rides a same-version
repack, whether or not any consumer is observed to depend on it. A consumer that binds against
`Fuaran.UI` 0.55.0 and loads 0.56.0 at runtime without recompiling is the case this version exists
to make visible.

**The mechanism, for the next reader who has to change the vocabulary.** Edit
`src/Fuaran.UI.Idl/Vocabulary.fs` (or `Support.fs`), then
`FUARAN_REGEN=1 dotnet run --project src/Fuaran.UI.Idl.Tests`, which rewrites `idl.json`,
`support.json` and `Generated.fs`. `dotnet run --project Build.fsproj -- Check` and every push
byte-compare the three. `scripts/sync-generated-layer.ps1` and its second-checkout CI job are
deleted; nothing outside this repository is read.

## Recorded change — 0.55.0, `Modal` gains a modality — the `Popover` variant (fuaran#1119)

**Additive on the wire in the strict sense that no shipped document's bytes move.** `ModalSpec` gains
`Modality: ModalityKind` (`Modal | Popover`, omitted on the wire at `Modal`) and `Anchor: string
option` (a NodeId, meaningful for `Popover` only). Every modal document written before this release
omits both, encodes to the bytes it always did, and decodes to the blocking dialog it always was.

**Source-breaking in one narrow place**, the 0.50.0 / 0.53.0 / 0.54.0 direction rather than the
0.51.0 one: a record gains fields, so a FULL-literal `ModalSpec` construction is `FS0764`.
`Defaults.modal` fills both (`Modal` and `None` — which IS the wire identity), so a consumer that
builds through `{ Defaults.modal with … }` is unaffected. The C# veneer's `ModalOptions` gains two
`init` properties with the same defaults, so no existing C# or VB call site changes.

**A new public enum, `ModalityKind`**, deliberately closed at two. The axis it names is whether the
surface BLOCKS the page, and that question has two answers; a sheet, a drawer or a menu is a
PRESENTATION of one of the two, not a third modality.

**What did NOT change, deliberately.** Nothing on the wire names a pixel, an edge, an offset, a
placement preference or an event. Anchored placement, the flip at a viewport edge, the offset and
the light-dismiss gestures are renderer-owned and bounded, on the affordance→op charter's terms.
`Modal` behaviour is unchanged in every particular, including its scrim, its focus trap and its
`aria-modal="true"`.

**Two new validator codes, both Warning.** **FUARAN122** — a `Popover` that is not anchored, either
because it declares no `Anchor` or because the `Anchor` names an id the tree does not carry; one code
because there is one consequence (the surface falls to the in-flow static floor) and one remedy, with
the payload discriminating so a typo can be named. **FUARAN123** — an `Anchor` on a `Modal`, a dead
declaration nothing reads. Both Warning because both documents are well-formed, decode on every host
and render; what is wrong is that nothing else would say so.

**New render-obligation claim: `aria-modal-only-when-blocking`** — the inertness claim is emitted for
`Modal` and never for `Popover`, while both carry `role="dialog"`. Every host in the §11.0 roster
reports it UNCHECKED until it adopts the claim, which is the render-fidelity manifest working as
designed.

The normative cross-host contract is `WIRE_FORMAT.md` §3.6.11.

## Recorded change — 0.54.0, `FileUpload` drop target and paste ingestion (fuaran#1115)

**Additive on every axis, and wire-additive in the strict sense that no shipped document's bytes
move.** `FileUploadSpec` gains two `bool` members — `DropTarget` and `AcceptPaste` — each omitted on
the wire when `false`. Every upload document written before this release omits both, encodes to the
bytes it always did, and decodes to a control that renders exactly as before.

**Source-breaking in one narrow place**, the 0.50.0 / 0.53.0 direction rather than the 0.51.0 one: a
record gains fields, so a FULL-literal `FileUploadSpec` construction is `FS0764`. `Defaults.fileUpload`
fills both (with `false`), so a consumer that builds through `{ Defaults.fileUpload with … }` — the
supported route, and the one the estate's own `SpecConstruction` guard requires at an authoring site —
is unaffected. The C# veneer's `FileUploadOptions` gains two `init` properties, both defaulting to
`false`, so no existing C# or VB call site changes.

**What did NOT change, deliberately.** No new `Action` case, no new handler slot, no new
`FormFieldKind`, and no new server-driven event name: a dropped or pasted file resolves through the
same `OnSelect` / `FileSelection` path a picked one does, and the server-driven boundary's existing
`change` / `file-read` rows carry both routes because a conformant client writes ingested files into
the control's own file input rather than dispatching around it. The gestures themselves — the drag,
the drop, the paste, the visible drop state — are renderer affordances named nowhere on the wire.

**New validator code: FUARAN121 (Warning)** — a `FileUpload` declaring either gesture while carrying
no `onSelect`. Warning rather than Error because a tree assembled before its handler is wired is an
ordinary authoring step; reported at all because a drop that lands nowhere has no user-agent feedback
of any kind, where a handler-less picker at least leaves the chosen filename in the browser's own
chrome.

**New render-obligation claim: `picker-always-present`** — the file picker and its label are emitted
whatever gestures a document declares. Every host in the §11.0 roster reports it UNCHECKED until it
adopts the claim, which is the render-fidelity manifest working as designed.

The normative cross-host contract is `WIRE_FORMAT.md` §3.6.10.

## Recorded change — 0.58.0, the live-Transform incremental seam (fuaran#1179)

**Purely additive, and additive in the shape that carries no `FS0764` risk at all:** three NEW
surfaces in `Fuaran.UI.ServerDriven` — the `LiveTransformStore` class, the `LiveTransformEvaluation`
record it returns, and the `LiveTransform` module beside them. No existing type gains a member, no
existing signature moves, and no wire vocabulary is touched: a document written before this release
encodes and decodes to exactly the bytes it always did, because nothing here appears on the wire.

**What it is.** A `TransformSource.Live` binding runs a pipeline over a state-bound table and is read
again whenever that state is written; evaluating it in full on every write is correct and pays for
every unchanged row. The columnar substrate ships a seam that avoids exactly that — prime once over
the source, then advance the primed state against a delta describing what the edit changed — and this
is its first consumer in the tier. `LiveTransformStore.Evaluate` primes on the first sight of a site
and advances on every later one, deriving the delta itself by diffing the source the primed state was
last evaluated against with the one handed in now.

**Why this tier and not the renderer.** The seam needs somewhere to keep the primed state BETWEEN
evaluations and the render path has nowhere: a binding-resolver call is a pure function of the sources
it is handed, by design and worth keeping. The server-driven tier already holds a connection's state
across edits — an inbound event IS a state edit — so the store lives there and is owned by whatever
holds the session. **The renderer's own `TransformSource.Live` path is unchanged and still evaluates
in full**; routing it through a session-held store would mean a new field on `BindingSources`, which
is a source-breaking change to the record every host constructs and belongs to its own release.

**What it promises, and what it does not.** It promises one thing: the table it returns is the table a
full evaluation over the current source produces. That is a certified property of the substrate's seam
rather than an assertion added here, and it is re-checked in this repository against the
`incremental-recompute` conformance family's own edit streams — because a consumer that took a
certified property on trust would not notice the day it stopped holding. It does NOT promise to have
done less work: a pipeline carrying a step whose output for a row depends on rows the delta does not
name falls back to the reference evaluator inside the seam, which reports the fall-back and its typed
reason in the returned footprint. A caller that wants to know before evaluating asks the substrate's
own plan, never the footprint.

**Bounded, and single-threaded by contract.** The store reuses `FragmentMemo.BoundedLru` rather than
minting a second cache, so the bound, the recency rule and the hit/miss counters are the ones the
fragment memo already has, and its threading contract travels with it: a host sharing one store across
threads serialises access. Eviction is never a correctness question — the next evaluation of an evicted
site re-primes and produces the same table, having paid for it.

**No new validator code and no new render-obligation claim.** Nothing here is authorable, so there is
nothing for a validator to report on and nothing for a host to render.

---

## Recorded change — 0.64.0, `Action.Print` and the print stylesheet (fuaran#1124)

**Additive on the wire; SOURCE-BREAKING at every exhaustive `match` over `Action`; a
`WIRE_FORMAT.md` §11 forward-coupling event.** Every pre-0.64.0 document encodes and decodes to
byte-identical bytes: nothing is removed, no member changes meaning, and no existing case's encoding
moves. The stylesheet half changes no markup at all — it adds a `@media print` block, so a screen
rendering is unchanged by construction rather than by inspection.

**`Action.Print` — an additive DU case, and the vocabulary's first PAYLOAD-FREE one.**
`{"$type":"Print"}` is the whole encoding. It says one thing — open the reader's own print dialogue —
and takes nothing to say it, because printing's parameters (page size, margins, orientation, sheet
range, copies, printer) belong either to the host's page setup or to the dialogue the reader is
operating. The paged MEDIUM is Host chrome under the ratified `PrintLayout` / `PageBreak` charter row;
what a document may state is *print now*.

  * **Source-breaking surface:** a new `Action` case breaks every exhaustive `match` over the DU,
    which is a source break under this repo's `TreatWarningsAsErrors` even though this document
    classifies DU exhaustiveness as non-breaking (the `FormFieldKind.DateRange` precedent, 0.7.0).
    It therefore rides a minor bump and never a re-pack of 0.63.0. Consumers matching `Action`
    exhaustively add one arm; consumers with a wildcard are unaffected.
  * **Two smaller additive DU widenings ride with it**, each source-breaking in the same way and for
    the same reason: `Fuaran.UI.Renderer.Runtime.ActionDescriptor` gains `Print` (the gate must be
    able to name what it is refusing) and `Fuaran.UI.StructuralQuery.Act` gains `Print` (a query
    surface that cannot ask about a case is a hole in the search vocabulary, not a smaller one).
    `Fuaran.UI.ServerDriven.ClientEffect` gains `Print` on the same terms.
  * **`IFuaranRuntime` is UNCHANGED**, deliberately, and it is the decision most likely to be
    revisited so it is recorded rather than left implicit. `window.print()` is the browser's own, takes
    no arguments and is present on every browser host, so a seam member would be a port for a
    capability no host can fail to provide — the `Action.CommitLocal` precedent, which likewise
    reaches `Browser.Dom.window` directly with no member behind it. Adding one later would be an
    interface widening under the pre-1.0-minor-add precedent this document already records; not adding
    one now costs nothing and keeps every existing implementer compiling.

**One decoder refusal, and it is the arm's asymmetry rather than a general tightening.** A member
beside `$type` on a `Print` is `WRONG_TYPE` at that member's path, and is NOT dropped. Everywhere else
in this format an unrecognised member is one the reading host has not learned yet; here there is
nothing to learn, so accepting `{"$type":"Print","pageRange":"1-3"}` would leave an emitter believing
it had constrained a printing it had not. The JSON Schema mirrors it — the `Print` branch is the only
one in the `Action` union carrying `additionalProperties: false`.

**A server-driven host LOWERS it; it does not refuse it.** `Driver.interpret` maps it to
`ClientEffect.Print` and the shim calls `window.print()`, exactly as it does for
`Action.WriteToClipboard`. Printing is an act of the machine the document is READ on, so a server
answering for its own process would be answering the wrong question. Nothing round-trips back: the
call reports neither whether the reader printed nor what they chose, so unlike `ClientEffect.ReadFileBody`
there is no result `LiveEvent` and a server never learns that a page was printed. The resumability path
classifies it `Interpret` — no closure, no module chunk, no host consultation.

**It is GATED, and it is not a hatch.** `ActionDescriptor.Print` joins the default-deny set, because an
unbidden modal that steals focus and on some platforms begins a physical act is host-observable however
little it discloses. What it discloses is the least of any gated action: no payload in either direction,
no text withheld because there is no text, and no result. A host with a deny-all policy refuses it
through the same `CanDispatch` seam it refuses `Call` / `Navigate` / `AiTool`.

**The reference stylesheet gains a `@media print` block of DEFAULTS, and no class names.**
`Theme.vocabularyFingerprint` is therefore UNCHANGED at `fv1:e697c1d1c162b9a7` — the digest is over the
class vocabulary, and this block styles classes the sheet already emitted. The four tier copies are
re-generated with the sheet (`Build.fsproj -- Css`) and `CssCheck` covers them as always.

The block is DEFAULTS only, and it is ordered BEFORE Phase 1473's authored break-control block so an
authored `keepTogether` / `breakBefore` wins at equal specificity. Three things it deliberately does
not do, each pinned by `PrintCascadeTests` so a later edit fails rather than quietly reversing the
meaning of the sheet: no `@page` rule (page geometry is the reader's, chosen in their own dialogue),
no `print-color-adjust: exact` (forcing fills to print would spend the reader's ink to rescue a tone
channel that must not have depended on colour), and **no blanket row cohesion** — a
`tr { break-inside: avoid }` default would make `DataGridSpec.keepRowsTogether`, admitted one release
earlier, change no rendering on any grid, and a shipped wire member whose declaration does nothing is
a fake affordance.

**No new validator code.** There is nothing about a payload-free action a whole-tree rule can report:
it has no slot to be inconsistent with, no counterpart to be unpaired from, and no destination to be
dead. A `Print` on a button is exactly as meaningful as a `Print` anywhere else the wire admits an
action.

## Recorded change — 0.65.0, the grid export affordance (fuaran#1125)

**Additive on the wire; SOURCE-BREAKING at every exhaustive `match` over `ActionDescriptor` and at
every full-literal construction of `DataGridSpec` / `GridSpecOf`; a `WIRE_FORMAT.md` §11
forward-coupling event.** Every pre-0.65.0 document encodes and decodes to byte-identical bytes:
nothing is removed, no member changes meaning, and the new member is omitted at `false`, which is the
value every existing grid has. Every pre-0.65.0 grid renders byte-identical DOM, because the control
and its wrapper are emitted only where the flag is declared.

**`DataGridSpec.exportable` — one additive `bool`, omitted at `false`.** It declares that this grid's
rows are the reader's to take: the renderer draws an export control and, on activation, serialises the
rows the client holds to RFC 4180 CSV and hands them over as a file.

  * **Source-breaking surface:** a record gains a field, so every FULL-LITERAL construction of
    `DataGridSpec` stops compiling (FS0764). Consumers building grids through `Fuaran.grid`,
    `Defaults.grid` or `{ spec with … }` are unaffected. It therefore rides a minor bump and never a
    re-pack of 0.64.0.
  * **`GridSpecOf` gains the slot LAST in declaration order**, which is not a style preference: that
    record has a POSITIONAL constructor the language veneers use, so a field inserted anywhere but the
    end would silently move an existing argument. The C# veneer's `DataGridOptions.Exportable` and the
    VB dialect's `exportable` attribute are added in the same change-set, per §11 step 6.
  * **`Fuaran.UI.Renderer.Runtime.ActionDescriptor` gains `Export of nodeId: string`**, source-breaking
    at every exhaustive `match` in the same way `Print` was one release earlier, and for the same
    reason: the gate must be able to name what it is refusing. It carries the grid's node id so a host
    policy can be per-grid rather than all-or-nothing, exactly as `SetState` carries its key.

**It is GATED, and the reasoning is not the same as `Print`'s.** The act is reader-initiated and the
content is data the reader is already looking at, so nothing is disclosed and nothing is transferred
that was not already on their screen. Two things still put it in the default-deny set. It **puts a
file on the reader's disk**, which is the only effect in that set that outlives the page; and the file
is **named by the tree**, so a decoded tree from an untrusted emitter chooses what appears in a
download list. A host with a deny-all policy refuses it through the same `CanDispatch` seam it refuses
`Call` / `Navigate` / `AiTool`; every default runtime allows it and behaves exactly as it did before
the affordance existed. Nothing is read back — the export returns unit, so the tree never learns
whether the reader kept the file.

**No new delivery mechanism, and that is deliberate.** The hand-over is the url-plus-suggested-name
pair the platform's existing download instruction already carries, performed the way that instruction
is performed: an anchor with a `download` attribute, activated and discarded. Minting a second
spelling of *give this to the reader* would leave two paths for a future change to keep in step.
`ServerDriven.ClientEffect` is UNCHANGED for the same reason its `Download` case already exists.

**What is exported is what the CLIENT HOLDS, and the control says which that is.** The rows are the
grid's fully resolved, SORTED set — never the page on screen, because a reader who sorted a column and
then exported expects the file in the order they are looking at. Where the source is host-paged (a
`Query` whose `dependsOn` names the page key) that resolved set IS one page, and the control's
accessible name reads *Export this page (N rows) as CSV* rather than *Export N rows as CSV*. That
distinction is the declared-total ruling applied to a second slot: the tree cannot substantiate data it
does not hold, and a control promising a whole dataset while delivering a page is a fake affordance by
understatement. **A full-dataset export over a paged query is host chrome and stays out of the
language.**

**The cells are exported FORMATTED, not raw, and the cost is recorded rather than hidden.** A cell
carries the text the reader is looking at — the column's own value projection through the column's own
declared `CellFormat`. The export is the reader's copy of the grid in front of them, so it carries that
grid's columns, their order, the sort order and the formats, which are exactly what the grid uniquely
holds. The consequence is real: a currency-formatted column exports as text a spreadsheet will not sum.
The reopen route is a declared export projection on the column, which would need its own demand and its
own charter walk.

**The serialiser emits a UTF-8 byte order mark; the grammar function does not.**
`GridExport.serialise` is RFC 4180 with CRLF record separators, no trailing separator and no mark;
`GridExport.document` is that behind U+FEFF. RFC 4180 says nothing about a BOM and a standards-lawyer
reading would omit it, but the acceptance here is that the file OPENS CORRECTLY IN A SPREADSHEET, and
the most widely used desktop spreadsheet decodes a BOM-less UTF-8 CSV in the ambient code page. Both
halves are pinned by tests, ordinally — the mark is a zero-width IGNORABLE character, so a
culture-sensitive `StartsWith` reports that every string begins with one, which is a probe that agrees
with whatever you expected.

**The SSR floor draws NO control, and the markup is byte-identical to a grid that declares nothing.**
An export is a gesture plus a file made on the reader's machine, and a static document has neither —
the CSV is built from the rows the client resolved, in the order the reader sorted them into, and a
server holds neither fact at the moment it matters. Emitting the control inert would advertise a
download the page cannot perform. The flag rides the wire to the client tier where it is acted on, so
there is no hydration question: the client's first render ADDS markup the server never claimed. The
no-script reader is not left without a route — a static grid has already emitted every row as a real
`<table>`.

**FUARAN131 (Warning) — an export with nothing to export.** Two statically-certain shapes under one
code: a grid naming no row source at all, and a data-bound grid declaring no columns (the columns ARE
the file's fields). A grid whose source merely RESOLVES to no rows is deliberately not reported — how
many rows a source yields is a runtime fact, and the export of an empty grid is a header record, which
is a true statement about the data rather than a failure. Go-red twins for every arm.

**`Theme.vocabularyFingerprint` → `fv1:8b3d17a39ac85085`.** Two classes entered the vocabulary —
`fuaran-grid-exportable` (the wrapper) and `fuaran-grid-export` (the control) — and the reference sheet
gained the rules that style them, including the focus ring. A host pinning the old value would accept a
sheet that knows neither and render a control that is not merely plain but keyboard-invisible. The four
tier copies are re-generated with the sheet (`Build.fsproj -- Css`).

---

## Recorded change — 0.66.0, the clipboard payload widens to a `TextSource` (fuaran#1126)

**BREAKING at every construction site of `Action.WriteToClipboard`; WIRE-NEUTRAL for every document
ever written; a `WIRE_FORMAT.md` §11 forward-coupling event.** This is the first entry in this
document to record a case-payload WIDENING rather than an addition, and the two halves of the
sentence above are the whole of it: the F# API changed and the bytes did not.

**`Action.WriteToClipboard of text: TextSource`, was `of text: string`.** The thing a reader
actually copies — a figure in the grid in front of them, a link the session holds — had no spelling,
because the payload could only be a literal the author typed at authoring time. It now resolves at
DISPATCH time through the standard binding resolution.

  * **Source-breaking surface, and how to repair it:** every construction site stops compiling
    (`FS0001`, not a warning), and the repair is mechanical — wrap the old argument in
    `TextSource.Literal`:

    ```fsharp
    // before 0.66.0
    Action.WriteToClipboard shareUrl
    // 0.66.0
    Action.WriteToClipboard(TextSource.Literal shareUrl)
    ```

    In C#, `Fuaran.UI.CSharp.FuaranAction.WriteToClipboard(Text)` is new in this release and takes
    the implicit `string` → `Text` conversion, so the literal case reads exactly as it would have
    with a string parameter. **Pattern MATCHES on the case are unaffected in count** — the arity is
    unchanged — but a match that bound the payload as a `string` and used it as one now binds a
    `TextSource`; `renderText` / `BindingResolver.resolveTextSource` is what turns it back into text.

  * **The wire did not move, and that is a fact about `TextSource` rather than a compatibility
    shim.** `TextSource.Literal`'s canonical form is the BARE JSON STRING
    (`WIRE_FORMAT.md` §3.6, one of the two 0.2.0 exceptions), so
    `{"$type":"WriteToClipboard","text":"https://…"}` is exactly what the encoder emitted before this
    release and exactly what it emits now, and the decoder reads it through the same §16 shorthand
    every text slot in the language shares. No shipped document breaks; no corpus fixture moved;
    `nodes/btn-copy-link.json` is byte-identical across the bump. What is NEW on the wire is a `text`
    carrying `{"$type":"Bound",…}` or `{"$type":"I18n",…}`, and the §16 normalisation of the explicit
    `{"$type":"Literal","text":…}` envelope at this slot
    (`lenient/lenient-1126-clipboard-literal-envelope`). A wrong-typed payload is `WRONG_TYPE`
    (`reject/reject-wrongtype-clipboard-payload`), never coerced.

  * **Why widen rather than add `WriteToClipboardBound`.** The cheaper spelling on this document's own
    cost model — see `docs/VOCABULARY.md` §2.1, which says plainly that widening a case is the most
    expensive of the three shapes — would have been a sibling case. It was declined, and the charter
    now carries the discriminator: a sibling is right when it names a DIFFERENT intent, and widening
    is right when it names the SAME intent over a wider range of inputs. Two cases for one intent is
    a permanent near-synonym pair, on the wire, in every host's `match`, and in the confusion matrix
    — where the widening's cost is a compiler error each construction site sees once.

**The construction sites this release changed, named so a consumer can see the shape of its own
work:** the catalog sample's copy button, the C#-authoring proof-of-concept's `Act.WriteToClipboard`,
the JSON-decode fixture and property generators, three op-stream test suites, the server-driven
action-log census suite, and the language-tier clipboard tests. Every one of them was a
`TextSource.Literal` wrapper and nothing else.

**`Fuaran.UI.Renderer.BindingResolver.resolveTextSource` is new and public** —
`BindingSources -> TextSource -> string`, the one definition of that dispatch in the estate. It is
additive, but it is worth naming here because it REPLACED two byte-identical hand-written copies (the
client renderer's `renderText` and the SSR renderer's), which is exactly the divergence
`ScalarSsrParityTests` was written to detect. A third copy was what this phase would otherwise have
added. Semantics are unchanged in every arm.

**`Fuaran.UI.ServerDriven.DriverServices` gains `ResolveText: TextSource -> string`**, source-breaking
at every FULL-LITERAL construction of that record (`FS0764`) and at nothing else — hosts building
services through `DriverServices.create` / `createPermissive` are unaffected. The driver resolves the
payload BEFORE lowering it, and `ClientEffect.WriteToClipboard` still carries a plain `string`: the
client shim performs a write, it does not evaluate a tree. The `create` default resolves against empty
sources, which is the identical dispatch a renderer performs for an unconfigured host — a literal
payload resolves to itself.

**The RESUMABILITY disposition now depends on the payload, and it is the only `Action` case for which
that is true.** `Resume.disposition` returns `Interpret` for a literal payload — unchanged, the
zero-JS path hands the runtime a string it already holds — and `Fallback` for a bound or i18n one, on
the `Call` reasoning: the resume interpreter holds no binding sources and no i18n catalogue, so it
cannot say what the payload stands for. Interpreting it anyway would put the DECLARATION on the
reader's clipboard rather than the value, which is a copy that silently succeeds with the wrong
content. The client interpreter carries the matching guard and warns rather than writing
`[object Object]` if an envelope and an interpreter ever disagree.

**Structured paste into an editable grid is NEW, is renderer-owned, and adds nothing to the wire.** A
grid that already declares `editable` plus an edit destination (`editStateKey`, or the Phase-663
State-source floor) has said its cells are the reader's to change; pasting a tab- or comma-separated
block writes through that same destination, in ONE write, under the same dispatch gate and
host-reserved-key guard a typed edit crosses. There is deliberately no `pasteable` flag beside
`exportable` — that member had to be declared because taking a grid away as a file is a capability
nothing else implies, whereas writing a cell is already declared, so a flag would be a second spelling
of a granted permission. Four decisions are pinned by tests rather than asserted in prose
(`Fuaran.UI.Renderer.GridPaste`): a TAB anywhere selects TSV and turns CSV quoting off, because that
is what desktop spreadsheets write; overflow past the grid's edges is DROPPED and never grown, because
the format has no row-insert and a `Query`-sourced grid's rows are the host's; a column with no edit
destination SWALLOWS its field rather than shifting the rest left, which is the failure that looks
like it worked; and a value that does not parse for its column is dropped for that cell alone. A
single-value paste is left to the browser entirely.

**No clipboard READ was added, and the decline is recorded rather than left as an absence.** A tree
that could sample the clipboard at a moment of its own choosing takes whatever the reader last copied
— a password, a one-time code, an address meant for somewhere else — without asking. Paste is
user-initiated by construction, and that gesture is the consent no wire member could manufacture. The
ruling is `docs/VOCABULARY.md`, Appendix A, Interaction / affordance cluster.


## Recorded change — 0.67.0, the rating and colour form fields (fuaran#1130)

**ADDITIVE to the wire, EXHAUSTIVENESS-BREAKING in F#, and a `WIRE_FORMAT.md` §11 forward-coupling
event.** Two `FormFieldKind` cases — `Rating` and `Color` — land together, which is why one entry
covers both: they are one change-set, one corpus sweep and one §11.2 vocabulary attestation.

**`FormFieldKind.Rating of allowHalf: bool * max: int * onChange: (float -> Action<'Msg>) option *
value: Binding<float> option`** — a subjective score on a small ordinal scale.
**`FormFieldKind.Color of onChange: (string -> Action<'Msg>) option * value: Binding<string> option`**
— the platform's own colour picker, over the canonical `#rrggbb` form.

  * **Every document written before 0.67.0 encodes and decodes to byte-identical bytes.** Neither
    case changes an existing one; no member moved; no default changed. A `Rating` or a `Color` in a
    document is new vocabulary, and a host that predates it refuses it as an unknown `$type` — which
    is the correct answer and the reason §11.2 attestation exists.

  * **Source-breaking surface, and it is the ordinary one for a closed DU.** `FormFieldKind` is
    exhaustively matched across the language tier, both renderers, the server-driven tier and the
    apply engine, so an EXHAUSTIVE match over it in a consumer's own code stops compiling (`FS0025`,
    an error under this repo's settings) until it handles the two new cases. A match with a wildcard
    arm is unaffected. Construction sites are untouched — nothing existing changed arity.

  * **Minor and not major, per this document's pre-1.0 rule**, the same classification the `Toggle`,
    `DateRange` and `Combobox` case additions took.

**The three decisions this release pins, recorded because a later reader will otherwise reopen them:**

  * **`Rating.value` is `Binding<float>`, and `allowHalf` governs ENTRY only.** The commonest rating
    a reader sees is an AVERAGE arriving through a `Query` binding — 4.3 of 5 — so the float is
    load-bearing even where nobody can type a fraction. Entry is whole units unless `allowHalf` says
    otherwise; display is continuous always. Those are two questions and the field separates them.
    `allowHalf` is a bool rather than a `step` because a `step` slot would admit `0.3` — a valid
    document naming an interaction no rating control has ever had — and would give `Rating` and
    `RangedNumber` a third member in common.

  * **`Rating.max` is REQUIRED, and its lower bound is a DECODE refusal while the value's bounds are
    not.** A `max` below 1 names a control that cannot exist, so it is refused where it is read (the
    `Switch.autoAdvanceMs` line). A value outside `0 .. max` is not refused at decode, because a
    bound value is invisible to a decoder and a rule enforced only on literals would be two rules
    wearing one name: `FUARAN132` (Warning) holds the static half and the server-driven submission
    floor holds the submitted half.

  * **`Color` admits `#rrggbb` and nothing else, refused rather than coerced, in three places.** The
    decoder refuses a `Static` literal outside that shape; `FUARAN133` (**Error** — a tree carrying
    one encodes to a document no conformant host will read back) refuses it for an author; and the
    server-driven floor refuses a submitted one. Case is preserved and never normalised, so
    `#FFAA00` round-trips byte-identically. Note this admits a CONTROL and says nothing about the
    `color` *rule format*, which the vocabulary charter declined for want of evidence and which
    remains declined.

**Two new validator codes**, both additive: `FUARAN132` (Warning — a static rating outside its own
scale) and `FUARAN133` (Error — a static colour that is not `#rrggbb`). A consumer that enumerates
`PreEmitDefect` exhaustively gains two cases.

**Class vocabulary moved**, so `Theme.vocabularyFingerprint` is restamped to `fv1:6702894e3f667a62`
and the four tier CSS copies are regenerated with it. A host asserting the old fingerprint refuses
the new sheet, which is the check working.

**§11 step 6 authoring surfaces:** C# `FormField.Rating` / `FormField.Color` and `Filter.Rating` /
`Filter.Color`; VB `kind="rating"` (with `max` / `allowHalf`) and `kind="color"` on both `<Field>` and
`<Filter>`; both analyzer vocabulary rows.


## Recorded change — 0.68.0, the media-capture upload field (fuaran#1116)

**ADDITIVE to the wire, source-breaking only at a full-literal construction, and a `WIRE_FORMAT.md`
§11 forward-coupling event.** `FileUploadSpec` gains `Capture: CaptureSource option`, and
`CaptureSource` is a new closed two-case enum (`Camera | Microphone`) whose wire spelling is a bare
string in the `capture` member.

  * **Every document written before 0.68.0 encodes and decodes to byte-identical bytes.** The member
    is absent-at-`None`, `Defaults.fileUpload` sets it to `None`, and `nodes/upload-1.json` is
    unchanged in the corpus. The field is APPENDED to the generated record, so no existing
    constructor position moves.

  * **The break is FS0764 at a full-literal `FileUploadSpec` construction, and nowhere else.** A
    consumer building the record through `{ Defaults.fileUpload with … }` — which is how the samples,
    the fixtures and every authoring veneer build it — is unaffected. No DU gains a case, so there is
    no exhaustiveness break: `CaptureSource` is a NEW type, and nothing matches on it yet.

  * **It is the HTML `capture` attribute and nothing more.** The renderers project the declared
    device onto the file input as `capture="environment"` (`Camera`) or `capture="user"`
    (`Microphone`) — both conforming enumerated-attribute keywords — beside the `accept` the document
    already declared. No stream, no live preview, no recording surface and no standing permission is
    introduced; the request is mediated by the same picker the control already had, and a platform
    with no such device ignores it and shows the file browser. `getUserMedia` and screen capture stay
    declined as Host chrome (`docs/VOCABULARY.md`, Appendix A — the `ScreenCapture` / `CameraInput`
    row, amended by this phase so the boundary is stated on both sides).

  * **An OPTION, not an omit-at-default enum.** "Say nothing" is a state of its own: an upload naming
    no device asks for the file browser, which is not one of the two devices wearing a default. A
    value outside the pair is `UNKNOWN_DU_CASE` at the bare slot and is never read as either device.

  * **The `capture`/`accept` pair is REPORTED, never repaired.** The two members are one statement —
    the keyword asks the platform for a recording device and `accept` decides which — so no renderer
    synthesises the missing half. A synthesised filter would be one the document did not write, would
    make emitted markup depend on renderer defaults, and would silently fix the case most worth
    reporting: an empty `accept`, which admits the device without selecting it.

**One new validator code**, additive: `FUARAN134` (Warning — a declared capture device the `accept`
list does not select). A consumer that enumerates `PreEmitDefect` exhaustively gains one case.

**One behavioural correction on the server renderer**, in the same change-set and for this feature's
sake: the SSR file input now emits `accept`, which it had never emitted while the client renderer has
emitted it since 0.x/Phase 130. It is required here rather than opportunistic — `capture` without
`accept` leaves the platform to guess the device — but it does change the SSR bytes of every upload
that declares an `accept` list. No class vocabulary moved, so `Theme.vocabularyFingerprint` is
unchanged and no CSS copy is regenerated.

**No render-fidelity obligation is added**, on the 0.67.0 precedent: a new claim puts every roster
host into "unchecked", which is a separate deliberate act.

**§11 step 6 authoring surfaces:** C# `FileUploadOptions.Capture` (a nullable `CaptureSource`); the
VB `capture` attribute on `<FileUpload>`, read through `OptEnum` so absence stays the picker; and the
analyzer vocabulary row.


## Recorded change — 0.69.0, the multi-token form field (fuaran#1121)

**ADDITIVE to the wire, EXHAUSTIVENESS-BREAKING in F#, and a `WIRE_FORMAT.md` §11 forward-coupling
event.** One `FormFieldKind` case — `Tokens` — the multi-token input: several values accumulated as
removable chips, over a suggestion set that may be open, searchable, asynchronous, or absent
entirely.

* **Every document written before 0.69.0 encodes and decodes to byte-identical bytes.** The case is
  new; no existing case gained, lost or moved a member, and no default changed. The break is
  `FS0025` in a consumer's own exhaustive `match` over `FormFieldKind`, and nowhere else — which the
  Semver section above classifies as minor rather than major, deliberately.

**THE SPELLING IS `Tokens`, NOT `Tags`, AND THE CAUSE IS THE F# COMPILER.** The phase was chartered
as `FormFieldKind.Tags`, and `Tags` turns out to be a RESERVED union-case name in F#: the compiler
generates a nested static class `Tags` in every discriminated union to hold its case-tag constants,
so a case spelled that way is `FS1219` in ANY F# union, not merely in this one. Splitting the names —
`"Tags"` on the wire, `Tokens` in F# — was considered and DECLINED, because this vocabulary's IDL has
no case↔wire mapping for union cases at all (`Declare.enumWith` gives enums one; unions have none):
it would be new generator, schema, TypeScript-emitter and sampler machinery to produce exactly one
case whose wire token no host's own source can spell, and the reference host is what GENERATES the
corpus. The charter's reserved NAME stays `Tags`, which is what a reader searches for; the case, the
`$type` and every authoring surface say `Tokens`. Same ceremony as 0.67.0's `ColorPicker` → `Color`
correction, different cause.

**`allowFreeText` OMITS AT `true` HERE AND AT `false` ON `Combobox`, and that inversion is the one
thing a consumer is most likely to get wrong.** It is one rule rather than two habits: the default
follows the REQUIRED-NESS OF THE SET. `Combobox.options` is required, so a combobox always has a
candidate set and constrained is its resting state; `Tokens.suggestions` is OPTIONAL, so a token box
with nothing to suggest is the commonest shape rather than a degenerate one, and open is its resting
state. The consequence worth stating: `{"$type":"Tokens"}` is a complete, useful document — the plain
open token box — where the same omission on `Combobox` gives a constrained one.

**One decode refusal, and two rules that deliberately are not.** `allowFreeText: false` with NO
`suggestions` member is refused: no gesture could ever put a token into that field, so the document
names a control that cannot exist rather than a control with a bad value in it (the `Rating.max < 1`
line). Under the polarity above it is reachable only deliberately, which is what makes refusing it
right. DUPLICATES and MEMBERSHIP are NOT refused at decode, because both are properties of the VALUE
and a bound value is invisible to a decoder — a rule enforced only on literals would be two rules
wearing one name. They are held where the value becomes visible: `FUARAN136` (Warning — a repeated
static token) and `FUARAN135` (Warning — a closed field over a static and empty suggestion list) for
an author, and the server-driven submission floor for a client, which is the only one that is a trust
boundary. A coercion at none of the three.

**The token list is ORDERED and stays that way.** `value` is a `Binding<string list>` — the slot type
the multi-select `values` has carried since Phase 291 — and nothing sorts or de-duplicates it: the
chip order is a fact the reader can see, and de-duplicating on decode would silently repair a document
this specification calls wrong. The declarative write-back rewrites the WHOLE slot on every add and
every remove, which is what preserves that order on a decoded tree with no host code.

Every judgement the control makes — what an entry may become, what the list becomes, which
suggestions to show, what a chip reads as, and the SSR comma projection with its inverse — is a pure
function in `Fuaran.UI.Renderer.TokensModel`, which both renderers read: the .NET runner mounts no
DOM, so a model inside the React component would be unreachable by any test here, and the hydrated
control and the SSR floor must not disagree about what one token list IS.

**The a11y decision, recorded in `TokensModel`'s header.** The chip row is `role="list"` of
`role="listitem"` with a real `<button>` per chip, NOT a `role="listbox"` of `role="option"`: a
listbox is for choosing among candidates and these are the value already chosen; `aria-selected` has
no honest value on a chip; and the gesture a chip offers is removal, which is a button. The entry
input carries `role="combobox"` only where a suggestion source was DECLARED — an absent source and a
resolved-empty one are different facts, and a combobox role with nothing to expand is the overclaim
the SSR floor is forbidden to make. The SSR floor is one comma-separated `<input>`, with two limits
recorded rather than claimed: a comma-bearing token does not survive the projection, and
`allowFreeText = false` is not enforceable by a text input.

Class vocabulary moved: nine classes entered, `Theme.vocabularyFingerprint` restamped to
`fv1:609b5d83ca7fc9d3`, four tier CSS copies regenerated.

**No render-fidelity obligation is added**, on the 0.67.0 precedent: a new claim puts every roster
host into "unchecked", which is a separate deliberate act.

**§11 step 6 authoring surfaces:** C# `FormField.Tokens` / `Filter.Tokens` (with `allowFreeText`
defaulting to `true`, matching the wire), the VB `kind="tokens"` mapping on both `<Field>` and
`<Filter>` — reading the token list from a comma-separated `initial` and the suggestions from
`<Option>` children — and NO new analyzer vocabulary row, because the dialect attributes it needs
(`allowFreeText`, `initial`, `<Option>`) all already mean the right thing and a dialect attribute
that already exists is not duplicated under a second name.


## Recorded change — 0.70.0, the large-binary upload seam (fuaran#1117)

**ADDITIVE to the wire, source-breaking at a full-literal construction and at two exhaustive matches,
and a `WIRE_FORMAT.md` §11 forward-coupling event.** `FileUploadSpec` gains
`Destination: string option` — a HOST-REGISTERED destination id, never a URL — and a new portability
seam, `Fuaran.UI.Ops.UploadSink.IFuaranUploadSink`, arrives in `Fuaran.UI.Ops.Abstractions` beside
`IActionInvocationSink`.

  * **Every document written before 0.70.0 encodes and decodes to byte-identical bytes.** The member
    is absent-at-`None`, `Defaults.fileUpload` sets it to `None`, and `nodes/upload-1.json` is
    unchanged in the corpus. The field is APPENDED to the generated record, so no existing
    constructor position moves.

  * **Three source breaks, all pre-1.0 minor and each in a different place.** FS0764 at a
    full-literal `FileUploadSpec` construction (a consumer building through
    `{ Defaults.fileUpload with … }` is unaffected). FS0025 in a consumer's own exhaustive match over
    `Runtime.ActionDescriptor`, which gains `Upload of destination: string`. And FS0025 in an
    exhaustive match over `ServerDriven.Validation.RejectReason`, which gains
    `BodyReadRefused of nodeId: string * destination: string`. `IFuaranRuntime` gains no member, so a
    direct implementer of that interface is untouched.

  * **The member is a NAME the host registered, and the distinction from a URL is the whole design.**
    A wire document comes from an arbitrary emitter; an address here would let that emitter choose
    where a reader's file goes. A host resolves the id against its own sink's declared `Destinations`
    set and refuses an id the set does not contain — **with no fallback of any kind**: it is not
    tried as a path, as a URL, or against a default destination, because a fallback makes
    registration advisory. The empty string is refused at DECODE (`WRONG_TYPE` at
    `$.…destination`); an unregistered non-empty id is refused at DISPATCH, because whether an id is
    registered is a fact about the host and the same bytes must not be valid for one reader and
    invalid for another.

  * **Two refusals stand in front of a transfer and they are a gate and a resolution, not two
    gates.** `ActionDescriptor.Upload` is the gate — *may this tree cause an upload to this
    destination at all* — refused by every shipped runtime by default, exactly as `Call` / `Navigate`
    / `Export` are, and reached back by name through `Runtime.permissive`. The sink's `Destinations`
    set is the resolution. A host that allows the descriptor and wires no sink still uploads nothing.

  * **What reaches the document is a REFERENCE and never the bytes.** `UploadedRef` carries a
    sink-assigned id, a content digest, the accepted size and the recorded type — no URL, no local
    path — and the renderer writes it to the control's HOST-RESERVED state slot
    (`host.upload.<nodeId>`, under the Phase 782 prefix), so a tree-originated `SetState` can never
    forge an upload result. `onSelect` is UNCHANGED and fires from the input's own change exactly as
    it always did: the transfer is a second fact about one gesture, not a second spelling of it.

  * **`Action.ReadFileBody` is now documented as the SMALL-PAYLOAD path, and the two are mutually
    exclusive at the server-driven boundary.** A `file-read` event against an upload that declares a
    destination is refused (`RejectReason.BodyReadRefused`) — on both sides of the policy gate, since
    a discipline a permissive host can opt out of is a preference. `ReadFileBody` is not deprecated:
    it remains correct for a body a handler needs in hand, and wrong for size.

**The seam's chunking is the SINK's, not the wire's.** `IFuaranUploadSink.Upload` takes a whole
`FileSelection` and a progress callback; framing, resumption, retry and hashing are the transport's,
and a framing declared on this surface would be one every host inherits and none can change.
`UploadSink.InMemorySink` chunks observably (one progress report per declared chunk size) so the
claim is testable rather than asserted.

**No new validator code.** 1116 minted FUARAN134 because `capture` and `accept` form an incoherent
pair an author can write; this member has no such pair — the one judgement it carries (the empty
string) is a decode refusal, which is where a document that names a control that cannot exist
belongs.

**One class-vocabulary change**, so `Theme.vocabularyFingerprint` moves to `fv1:31d200d72663a91a` and
the reference stylesheet plus its four tier copies are regenerated: `fuaran-upload-stream` and its
three `role="status"` state classes (`-progress` / `-done` / `-refused`).

**No render-fidelity obligation is added**, on the 0.67.0 and 0.68.0 precedent: a new claim puts every
roster host into "unchecked", which is a separate deliberate act. The §11.0 roster gains a
streamed-upload adoption table instead, with `fuaran` adopted and every other host pending.

**§11 step 6 authoring surfaces:** C# `FileUploadOptions.Destination` (a nullable `string`); the VB
`destination` attribute on `<FileUpload>`, read through the plain `Attr` so absence stays the
client-only control; and the analyzer vocabulary row.

## Recorded change — 0.71.0, the op-stream sinks' compare-and-append and keyed append (fuaran#1485)

**ADDITIVE on every axis: no existing member changed signature, no DU gained a case, no record
gained a field, and `IOpStreamSink<'Msg>` gained nothing.** `Fuaran.UI.OpStream.Abstractions` gains
`AppendReceipt`, `CasAppendOutcome`, `KeyedAppendOutcome`, and two OPTIONAL extension interfaces —
`IOpStreamCasSink<'Msg>` and `IOpStreamKeyedSink<'Msg>`; `Fuaran.UI.OpStream.Replay` gains
`ApplyPersist.applyWithSinksKeyed`. Both shipped stores (`InMemorySink`, `SqliteSink`) implement
both new interfaces.

  * **Why the members are on extension interfaces rather than on `IOpStreamSink<'Msg>`.** That
    interface is shipped and implemented outside this repo; an abstract member added to it breaks
    every external implementor at compile time, which would make this a major change in everything
    but the version number. `IOpStreamCheckpointSink<'Msg>` established the shape here — inherit the
    base, add the members, let a consumer ask for the capability by type — and it is what keeps the
    addition free for a consumer who wants none of it.

  * **Two interfaces and not one, deliberately.** A compare-and-append and a keyed append are
    independently implementable: a store that can compare a head cheaply may carry no key index, and
    a queue with an idempotency header may have no addressable head. Both shipped stores implement
    both; a third-party sink is not made to claim what it does not do.

  * **`AppendIf` returns a VALUE where the old guard threw, and that is the point.**
    `CasAppendOutcome.StaleHead (expected, actual)` names the head the store actually holds, so a
    retry loop rebuilds its record against `actual` without a second round trip. Nothing is
    persisted on a refusal. The SQLite implementation wraps the head read and the insert in one
    transaction — outside one they are two statements a concurrent writer can interleave, which is a
    slower race rather than a compare-and-append.

  * **The NO-EXPECTATION path is unchanged, deliberately.** `Append` still takes no head, still
    THROWS on a duplicate `(StreamId, Sequence)`, and still accepts a record whose `PreviousHash`
    does not link to the current head — that mis-chain is caught on the READ path by
    `LoadVerification.Full`, exactly as before. A caller that wants the head checked asks for it by
    calling `AppendIf`. A duplicate sequence reached through `AppendIf` at a MATCHING head throws
    for the same reason it always did: it is a structural defect, not a stale head, and reporting it
    as one would tell the caller to retry a record that is already there.

  * **`AppendKeyed` is keyed on `(StreamId, invocationKey)` and answers a re-send from the key, not
    from the record.** The first call persists and returns `Appended receipt`; every later call for
    that key persists nothing and returns `Duplicate receipt` — the SAME receipt, whatever the second
    call's record says. The asymmetry is the contract: a caller that rebuilt its record after a lost
    acknowledgement carries a fresh timestamp and a re-derived sequence, and must still be told about
    the record it already has. The key is opaque to the sink; choosing one that separates genuinely
    distinct invocations is the caller's obligation, as it is for Core's `keyOf` projection.

  * **The SQLite store gains an `op_invocation` side table, not a column on `op_stream`.** Its
    `PRIMARY KEY (stream_id, invocation_key)` IS the unique index on the key, and the SELECT in front
    of it is only a fast path — a writer that takes the key in between is refused by the index, by
    name. **An existing database migrates by being opened**: `ensureSchema` runs
    `CREATE TABLE IF NOT EXISTS` on construction, no `op_stream` row is read or rewritten, and a
    database written by an older version is fully usable by this one and vice versa (an older
    version simply ignores the extra table). No migration step is required of a host.

  * **`applyWithSinksKeyed` takes the sink as `IOpStreamKeyedSink<'Msg>` rather than probing for the
    capability at run time.** A host cannot then ask for idempotency from a store with no key index
    and silently receive a plain append — the one outcome worse than not offering the contract.
    `applyWithSinks` is UNCHANGED and still cannot recognise a retry: nothing in an op says which
    invocation produced it, and two genuinely distinct user actions may carry the identical op, so
    the wrapper that was never given a key cannot invent one. The two entry points differ by exactly
    that fact.

**No wire-format change, no `WIRE_FORMAT.md` §11 event, no validator code, no class vocabulary.**
The addition is a durable-port contract; nothing about a `Node`, a `TreeOp` or their encodings moves,
so `Theme.vocabularyFingerprint` and the reference stylesheet are untouched and no corpus fixture is
regenerated.

**No render-fidelity obligation and no host-adoption roster row**, on the 0.67.0 / 0.68.0 / 0.70.0
precedent: the sink contract is a .NET-tier persistence seam, not a wire claim a sibling host could
conform to.

## Recorded change — 0.73.0, the `Binding` `Static` payload-presence split (fuaran#1140)

**Additive-but-visible change to the published wire-format JSON Schema
([`wire-format-fixtures/schema.json`](../wire-format-fixtures/schema.json), emitted by
`Fuaran.UI.Ops.SchemaGen`). No canonical JSON changes: no fixture payload moves, no encoder or
decoder behaviour moves, and every document that was legal before is legal now.** What changes is
what the schema is willing to say NO to, and one new `$defs` entry.

**The gap.** Phase 1068 gave every `Binding` slot a `$def` instantiated at its element type, so a
boolean at `Metric.trend` stopped being structurally well-formed. What it did not state is whether
the `Static` payload is PRESENT. `bindingDef` emitted the `Static` arm with `value` optional in all
ten instantiations — Phase 677's "absence is structural" — so `{"$type":"Static"}` validated against
`Binding_float` exactly as it validated against `Binding_json`, while the decoder routes the missing
key through the slot's OWN parser and the answer differs per slot: a scalar parser refuses the
resulting null, a collection parser normalises it to the empty collection, and a choice slot reads
it as "no selection". A schema-driven emitter that fills a kind's required fields and nothing else —
the shape every such walker takes, because required-ness is the only signal the dialect gives it —
therefore emitted the valueless form at every scalar `Binding` slot and had the decoder refuse the
node, with nothing in the artefact to tell it otherwise.

**What moved.**

  * **`Binding_bool` / `Binding_float` / `Binding_int` / `Binding_str` now carry `value` in their
    `Static` arm's `required` list.** The four element types whose parser has no reading for an
    absent payload. A document these now refuse is a document the decoder already refused with
    `WRONG_TYPE`, so no legal document became illegal — the schema caught up with the decoder rather
    than the other way round.

  * **`Binding_str_choice` is a NEW `$def`, and this is a SPLIT rather than a tightening.** The
    schema pointed `Choice.value`, `SegmentedChoice.value`, `Combobox.value` and `Select.value` at
    the same `#/$defs/Binding_str` as `TextArea.value`, `Link.href` and `Image.src` — but the
    decoder splits them: the control slots go through `decodeBindingChoiceValue`, where an absent
    `Static` payload is first-class ("no selection", the typed `Static None`). One `$def`, two
    contracts. A required-key edit to `Binding_str` would have started refusing four shipped accept
    fixtures (`controls-closure`, `form-segmented`, `multiselect-1`,
    `multiselect-chip-list-param`), so those four slots now `$ref` their own instantiation, whose
    `Static.value` stays optional and which carries a `description` saying what the absence MEANS —
    the one thing the structure cannot state.

  * **The collection instantiations and the two any-JSON abstentions are UNCHANGED**, deliberately:
    `Binding_list_SelectOption` / `_list_str` / `_list_float` / `_list_MapMarker` normalise an
    absent payload to the empty collection, and `Binding_json` / `Binding_hosted` pass it through.
    Requiring the key there would refuse documents the decoder accepts.

**Consumer impact.** A consumer that VALIDATES gains four refusals it previously had to get from the
decoder, and loses nothing. A consumer that WALKS the schema structurally must follow one more `$ref`
name (`Binding_str_choice`) and will now find `value` in `required` at the four scalar
instantiations — which is the point: it is what lets a schema-driven synthesiser emit a decodable
default at a typed scalar slot. A consumer that generates provider-native constrained emission from
the schema gets a strictly narrower grammar, all of whose removed shapes the decoder rejected.

**Pinned by** `src/Fuaran.UI.JsonDecode.Tests/BindingStaticPresenceTests.fs`, rewritten from the
inverse pin the same phase's first slice landed: three lists — the scalar slots (schema INVALID,
decoder ERROR), the four choice slots (schema VALID, decoder OK), and the absentable instantiations
(schema VALID, decoder OK) — each probe asserting BOTH sides of the seam and carrying its own
payload-present control node, plus a disjointness check that fails if the two string contracts are
ever re-merged. `WIRE_FORMAT.md` §13's Shape paragraph is amended to describe both axes; the
corpus's `schema.json` and this repo's byte-copied `docs/prompt-pack/schema.json` are regenerated.

**No `NodeKind` change, no `Theme.vocabularyFingerprint` event, no reference-stylesheet change, no
render-fidelity obligation.** The wire vocabulary is untouched; `idl.json` is byte-unchanged, which
is the expected result — the IDL always carried the element type, and presence is a property of the
schema's expression of it, not of the vocabulary.

## Recorded change — 0.74.0, `Action.Dispatch` marked in-process-only (fuaran#1152)

**A compile-visible surface change with NO wire change, no type change and no member change.** The
generated `Action<'Msg>` case `Dispatch` now carries a doc block and a single
`[<System.Obsolete(…, isError = false)>]` attribute, emitted by the generator from a declared
annotation. Mentioning the case raises **FS0044** in F# and **CS0618** in C# — a WARNING, which a
consumer escalates or scopes on its own schedule.

```fsharp
// Fuaran.UI.Generated — Action<'Msg>
/// **In-process only** — this member has no wire projection: a value here
/// is carried inside one host process and is LOST across any wire boundary.
| [<System.Obsolete("in-process only — no wire projection; a value here is lost across a wire boundary", false)>] Dispatch of msg: ('Msg)
```

**What it is for.** `Action.Dispatch of 'Msg` carries a host closure with no wire projection:
`{"$type":"Dispatch"}` is the whole encoding, and the decoder restores the payload as the
`"<closure>"` sentinel, so a host fed canonical wire JSON observes THAT an affordance fired and can
never receive the message. 0.47.0 made that legible in prose — a doc comment on `Action.dispatch`,
`PreEmitValidate.validateForTransport`'s **FUARAN112**, and `CanonicalJson.encodeNodeForTransport`'s
refusal — and deliberately stopped short of an attribute. This is the attribute, arrived at from the
other side.

**Why it is DECLARED rather than written.** The `Action` DU lives in `Generated.fs`, a byte-pinned
projection of `src/Fuaran.UI.Idl/`; an attribute hand-added there is erased by the next
regeneration. The marking is therefore `Annotations.InProcessOnly = true` on the `Dispatch` case in
`Vocabulary.fs`, which the IDL engine renders into three artefacts at once: the F# declaration
above, the `"inProcessOnly": true` key in `idl.json` (which is how a non-.NET host learns the fact),
and the doc block. Removing it is a source edit that a test names.

**Why `isError = false` is the whole design.** Every in-process construction of `Dispatch` in this
repo, in every sample, and in every downstream Fable host is CORRECT — a full-Fable tree is never
serialised, so nothing is lost. An unconditional error would be the compiler making a claim about
those sites that is not true, which is exactly why 0.47.0 declined a hand-written `[<Obsolete>]`
under this repo's `TreatWarningsAsErrors`. A warning states the fact and leaves the judgement with
the host: escalate with `--warnaserror:44` where trees are serialised, scope it where they are not.

**How this repo scopes it, and the three shapes it takes.** `TreatWarningsAsErrors` is on here, so
every mention had to be addressed rather than tolerated — and the shape differs by what the site
actually is:

  * **A total analysis of the union** — `NodeMap.mapAction`, `BindingWalk`'s three action walks and
    its state walk, `AffordanceInertness.actionFindings`, `StructuralQuery.carriedOf`,
    `ActionInvocation.describe` / `payloadFor`, `JsonDecode.decodeAction`,
    `CanonicalJson.encodeAction`, `Render.containsUnwiredAction` / `runActionCore`,
    `Resume.disposition` / `encodeAction`, `Driver.interpret` — carries a `#nowarn "44"` /
    `#warnon "44"` pair around the ONE declaration, with a comment saying why. These must name every
    case that exists; naming one is not authoring one. It is the same reason the generated module
    carries its own `#nowarn "44"`, which the engine emits for any vocabulary that marks anything.
  * **A test file** carries a file-scoped `#nowarn "44"` and a comment. Tighter is not available:
    the mentions sit inside `testList` expressions, where a lexical directive cannot be placed. A
    suite is not an authoring surface — those uses exist to PIN the marked case's behaviour.
  * **An authoring site takes the route with the paragraph attached, and suppresses nothing.**
    `samples/catalog/Tabs69.fs`, `samples/server-driven/{App,Form}.fs` and `samples/resume/gen` now
    call `Action.dispatch`, whose doc comment states the constraint and both remedies. The C#
    PoC's `Act.Dispatch` keeps the one suppression it needs and gains an XML doc saying the same
    thing, so the C# authoring surface is legible too.

`Action.dispatch` itself is deliberately NOT marked. Marking the helper would push FS0044 back onto
every correct in-process call site — the estate-wide acceptance 0.47.0 measured and declined — and
what a caller of the helper gets instead is a doc comment the compiler shows on hover, which says
more than a one-line attribute can.

**No wire-format change and no `WIRE_FORMAT.md` §11 event.** The discriminator `"Dispatch"` is
unchanged, the encoding is unchanged, the resume-boot marker is unchanged, and every fixture in the
conformance corpus is byte-identical. `idl.json` gains one key, which the corpus's copy carries too.
An annotation never changes bytes: the engine's artifact omits an empty annotation set entirely, so
no other member's projection moves either.

**Breaking-change classification: NOT breaking.** No signature changed, no DU gained a case, no
record gained a field. A consumer that neither escalates FS0044 nor already treats warnings as
errors sees a new warning and nothing else. A consumer that DOES treat warnings as errors and
constructs `Dispatch` will see a build failure and can scope it in one line — which is the
`isError = false` contract working as intended, not a break. Consumers on the .NET tier that author
in-process trees are the ones affected; a wire consumer never had the payload.

**No render-fidelity obligation and no host-adoption roster row.** The marking is a statement about
a .NET/C# DECLARATION; the fact it states is already normative in the wire format, and a sibling
host's own declaration is its own business. What the corpus's `idl.json` carries is the fact, not an
obligation to render it.
