# Fuaran host-styling checklist

This file enumerates the CSS variables and class hooks the Fuaran renderer emits, plus the contract a host stylesheet must honour to render Fuaran UI correctly. Companion of [`HOST-INTEGRATION-CHECKLIST.md`](HOST-INTEGRATION-CHECKLIST.md) (which covers host-integration-tier wiring). Forge consumers can drop the packaged `content/fuaran-reference.css` straight in; non-forge hosts and consumers with their own design tokens read this file end-to-end and bridge per [`docs/THEME-BRIDGE-GUIDE.md`](docs/THEME-BRIDGE-GUIDE.md).

Phase 12 session 3b shipped the reference stylesheet at `samples/demo/index.css`. Phase 12.H (this checklist's authoring) packs it as `content/fuaran-reference.css` in the `Fuaran.UI.Renderer` NuGet under Apache 2.0, completes the 7×N tone matrix for callout / progress / pill / metric, and turns the previously-implicit class contract into the document below. Phase 12.K (next) will migrate the variable surface into a typed `Theme` record – this checklist remains the canonical source for "what classes does the renderer emit".

The renderer NEVER emits Tailwind utility classes or inline hex colours. Every visual decision is reachable from a CSS variable (Section 1) and styled against a class hook (Sections 2–4). Two override paths are supported: re-binding variables at the `:root` layer (preferred) or replacing the reference stylesheet wholesale (kept open for §4l down-shift portability).

## 1. CSS variables

The renderer's reference stylesheet ([`src/Fuaran.UI.Renderer/content/fuaran-reference.css`](src/Fuaran.UI.Renderer/content/fuaran-reference.css)) binds these variables at the `:root` layer. Consumers override by re-binding at their app shell. Every variable reference in the reference CSS uses the `var(--X, fallback)` form so an unstyled-mode renderer (no consumer stylesheet, no reference stylesheet) still shows recognisable defaults.

### 1.1 Tone palette (7 × 3 = 21 variables)

| Tone | Background | Foreground | Border |
|---|---|---|---|
| `default` | `--fuaran-tone-default-bg` | `--fuaran-tone-default-fg` | `--fuaran-tone-default-border` |
| `subdued` | `--fuaran-tone-subdued-bg` | `--fuaran-tone-subdued-fg` | `--fuaran-tone-subdued-border` |
| `brand` | `--fuaran-tone-brand-bg` | `--fuaran-tone-brand-fg` | `--fuaran-tone-brand-border` |
| `success` | `--fuaran-tone-success-bg` | `--fuaran-tone-success-fg` | `--fuaran-tone-success-border` |
| `warning` | `--fuaran-tone-warning-bg` | `--fuaran-tone-warning-fg` | `--fuaran-tone-warning-border` |
| `critical` | `--fuaran-tone-critical-bg` | `--fuaran-tone-critical-fg` | `--fuaran-tone-critical-border` |
| `info` | `--fuaran-tone-info-bg` | `--fuaran-tone-info-fg` | `--fuaran-tone-info-border` |

`ToneVariant` cases at [`Fuaran.UI/Types.fs:607`](src/Fuaran.UI/Types.fs). The naming root is `--fuaran-tone-{toneVar}-{bg|fg|border}` where `toneVar` matches the lower-cased DU case ([`Theme.toneVar`](src/Fuaran.UI.Renderer/Theme.fs)).

### 1.2 Spacing scale (5 variables)

Lifted out of hardcoded pixels in Phase 12.H so consumers can override compact / spacious densities without forking the reference CSS.

| Variable | Reference value | Used by |
|---|---|---|
| `--fuaran-space-xs` | `4px` | spacer-small, fine gaps inside cells |
| `--fuaran-space-sm` | `8px` | row spacing, button vertical padding, default node margin |
| `--fuaran-space-md` | `12px` | stack default gap, card body padding, button horizontal padding |
| `--fuaran-space-lg` | `16px` | card padding, dashboard gap, panel padding |
| `--fuaran-space-xl` | `24px` | spacer-large, top-level container padding |

### 1.3 Typography scale (7 size + 4 weight + 3 line-height = 14 variables)

| Variable | Reference value | Used by |
|---|---|---|
| `--fuaran-text-xs` | `12px` | help text, table headers, grid pill, caveat, badge |
| `--fuaran-text-sm` | `13px` | Metric label, form label, tab body, callout, progress label |
| `--fuaran-text-base` | `14px` | body default, buttons |
| `--fuaran-text-lg` | `16px` | card heading |
| `--fuaran-text-xl` | `20px` | mid-sized headings |
| `--fuaran-text-2xl` | `24px` | large headings |
| `--fuaran-text-3xl` | `28px` | Metric value |
| `--fuaran-font-weight-regular` | `400` | body |
| `--fuaran-font-weight-medium` | `500` | buttons, table headers, Metric label |
| `--fuaran-font-weight-semibold` | `600` | headings, card heading |
| `--fuaran-font-weight-bold` | `700` | Metric value |
| `--fuaran-line-height-tight` | `1.25` | dense headings |
| `--fuaran-line-height-normal` | `1.5` | body default |
| `--fuaran-line-height-relaxed` | `1.75` | spacious mode (Phase 12.K) |

### 1.4 Component dimensions (7 variables)

| Variable | Reference value | Used by |
|---|---|---|
| `--fuaran-button-pad-y` | `var(--fuaran-space-sm, 8px)` | button vertical padding |
| `--fuaran-button-pad-x` | `var(--fuaran-space-md, 12px)` | button horizontal padding |
| `--fuaran-radius-sm` | `4px` | inputs, callouts, small surfaces |
| `--fuaran-radius-md` | `6px` | buttons, table |
| `--fuaran-radius-lg` | `8px` | cards, panels, Metric |
| `--fuaran-radius-full` | `9999px` | pills, badges |
| `--fuaran-border-width` | `1px` | every bordered surface |

### 1.4b Component extension points (14 variables) — fallback-only by design

These are per-component knobs a host may re-bind, and they are the one group the reference stylesheet does **not** declare at `:root`: each exists only as the fallback in its own `var(--X, fallback)` site. That made them effectively undiscoverable — a host reading `:root` could not tell that the code pane's colours or the modal's backdrop were overridable at all — which is why they are enumerated here.

**Why they are not simply declared at `:root`** (checked 2026-08-21, having first tried it): the reference sheet's `:root` block is **bijective with the typed `Theme` record**, and `ThemeTests`' "covers every `--fuaran-*` variable" test asserts the correspondence in *both* directions — a variable in `:root` that `Theme.toCss` does not emit fails the suite, as does the reverse. So declaring these fourteen is not a stylesheet edit at all; it is a decision to take them into the typed theme surface, which reaches the `Theme` record and its parser, the four byte-identical CSS copies, the theme-manifest bridge, and the brand re-bind. Worth doing deliberately; not something to slip in as tidying.

Re-binding one works exactly as for any other token — set it at your app shell and the `var()` site picks it up, the fallback being what applies when you do not.

| Variable | Fallback value (the effective default) | Used by |
|---|---|---|
| `--fuaran-font-mono` | `ui-monospace, "SF Mono", Menlo, Consolas, monospace` | code pane, inline code |
| `--fuaran-code-bg` | `#1e1e2e` | code-pane surface |
| `--fuaran-code-fg` | `#cdd6f4` | code-pane text |
| `--fuaran-code-copy-bg` | `#313244` | code-pane copy button |
| `--fuaran-code-copy-border` | `#45475a` | code-pane copy button border |
| `--fuaran-avatar-size` | `40px` | avatar width + height |
| `--fuaran-disclosure-summary-padding` | `var(--fuaran-space-md, 12px) var(--fuaran-space-lg, 16px)` | disclosure `<summary>` padding |
| `--fuaran-modal-backdrop` | `rgba(0, 0, 0, 0.5)` | modal scrim |
| `--fuaran-modal-max-width` | `560px` | modal dialog width cap |
| `--fuaran-toast-max-width` | `360px` | toast width cap |
| `--fuaran-shadow-lg` | `0 10px 15px -3px rgba(0, 0, 0, 0.1)` | raised surfaces (popover, toast) |
| `--fuaran-shadow-xl` | `0 20px 25px -5px rgba(0, 0, 0, 0.1)` | modal dialog |
| `--fuaran-z-modal` | `1000` | modal stacking order |
| `--fuaran-z-toast` | `1100` | toast stacking order |

### 1.5 Weight and emphasis: consumer hooks, not a pending gap

`StyleWeight` and `Emphasis` on `SemanticStyle` emit `fuaran-weight-{compact|standard|spacious}` and `fuaran-emphasis-{quiet|normal|loud}`, and the reference CSS carries **no rules for them, deliberately**. This entry previously read as a Phase-12.K to-do (compact / spacious against the spacing scale; quiet / loud against border-width + shadow); Phase 431, which made emitted-class ↔ rule coverage executable, **settled it the other way** and recorded the absence as a declared, tested one rather than an open promise.

The reason is the shape of these two axes rather than a shortage of design opinion: *every* node wrapper carries one class from each, so any rule here lands on every node in the tree at single-class specificity — it would race the per-component rules by file order — and a `standard` / `normal` rule restating a token's default value would defeat a consumer's own `:root` override of that token. Consumers safelist and bind these hooks at their app shell, which is what they are for. Giving them reference rules is a design decision with estate-wide visual blast radius; it is open to a deliberate later phase, but it is not a conformance defect.

The class hooks are documented in Section 2.0 below so Tailwind-JIT-shaped consumers can pre-safelist them.

### 1.5b Coverage is enforced, not documented (Phase 431)

The class contract in Sections 2–4 is executable. `src/Fuaran.UI.Tests/CssCoverageTests.fs` enumerates every class the renderer can emit — the Theme projections by *running* them over every DU case, the structural per-spec vocabulary by *scanning* the renderer sources — and fails the build when one has no rule in `content/fuaran-reference.css` and no declared absence. A declared absence names the class that carries the chrome instead, and the suite checks *that* class really is styled, so an exemption cannot be a mute-list entry. The same suite asserts the TypeScript tier's byte-copy of the stylesheet is identical, which is what carries that coverage proof across to the copy.

**So a new class hook is not shipped until it has a rule or an entry.** Adding one to a renderer without either turns the build red here, rather than surfacing as an unstyled node in a downstream host's page.

### 1.5c The tier copies are generated (Phase 432)

`content/fuaran-reference.css` is the **canonical** stylesheet; every other host tier ships a byte-copy of it. Those copies are **generated, not hand-copied**:

```powershell
dotnet run --project Build.fsproj -- Css          # rewrite every tier copy present in this checkout
dotnet run --project Build.fsproj -- CssCheck     # fail, naming every copy that is not byte-identical
```

`CssCheck` is wired into the `Check` gate, so an edit to the canonical sheet that has not been propagated fails for the author who made it. That is the point of running it from this side: each consuming tier already locks its own copy, but only that tier's suite sees the drift, in a repo the author was not in — which is how a preceding change left two copies serving a stylesheet two rule families behind. A tier whose repo is not in the checkout is reported as *not checked* rather than passing quietly.

### 1.5e Writing direction: the sheet is LOGICAL; the shell is not (Phase 1114)

Every inline-axis rule in the canonical sheet is expressed in CSS **logical** properties —
`margin-inline-start` / `padding-inline-end` / `border-inline-start` / `inset-inline-start` /
`inset-inline-end` / `inset-inline` / `text-align: start|end` / `float: inline-end`. The consequence
for a host is short: **set `dir` and the whole component vocabulary mirrors.** There is no RTL
stylesheet, no override block to import, and no per-component opt-in. A nested subtree that sets its
own `dir` mirrors only itself, which is what a mixed-direction page is made of — an Arabic page with
a left-to-right table of identifiers in it, say. The sheet's own header enumerates the three physical
usages that survive deliberately and why, and the only two `[dir="rtl"]` rules in it flip the
disclosure chevron's GLYPH, never any geometry.

Two things this does **not** do for you, and they are the whole of the host-side work:

1. **The `dir` attribute itself.** Nothing in the stylesheet emits it. A host derives it from the
   document's locale — in this repo `Formatting.textDirection : string -> string` is that
   derivation, and the Giraffe `DocumentShell` is its first consumer. A tier that ships the
   byte-copy inherits the mirroring and inherits **none** of this.
2. **A host that re-implemented the class hooks** rather than serving the sheet (the §1 option (b)
   path) must make the same physical→logical substitution in its own rules. Serving a mirrored
   vocabulary from a sheet whose own rules are physical produces a page that is half-mirrored, which
   is worse than one that is not mirrored at all.

**Per-tier handoff — enumerated here for [Phase 1128](../../roadmap/phases/1128-platform-baseline-host-adoption.md).**
The CSS moves by byte-copy; the shells do not, and each tier's shell is a different seam:

| Tier | Inherits the mirrored sheet | Still owes a `lang` / `dir` declaration |
|---|---|---|
| `fuaran-ts` | yes — `packages/renderer/css/fuaran.css` | its standalone-bundle mount and any host page template it ships |
| `fuaran-go` | yes — `renderer/content/fuaran-reference.css` | the static-HTML + islands document emitter (the `<html>` open tag it writes) |
| `fuaran-rs` | yes — `css/fuaran.css` | the server-side emitter and the `wasm32` client's mount root |
| `fuaran-py` | yes — `src/fuaran_py/renderer/content/fuaran-reference.css` | the server-HTML renderer's document wrapper |
| `fuaran-swift` / `fuaran-kt` | n/a — native surfaces, no stylesheet | the platform's own layout-direction setting, from the same locale |

Each of those shells needs the same two facts the F# shell now derives: the BCP-47 tag, and the
direction that follows from it. The derivation is small and deterministic (an RTL script set, an RTL
language-default set, script-subtag-wins) — port it, do not re-derive it, and do not reach for a
runtime locale database, because the answer must be the same string on the server that emits the
markup and the client that hydrates it.

### 1.5d The stylesheet carries a class-vocabulary fingerprint (Phase 433)

A host serves its stylesheet from `Fuaran.UI.Renderer` and — if it server-renders — emits its classes from `Fuaran.UI.Renderer.Server`. Nothing couples those two package versions. A host that pins one and serves the other's sheet gets no error at all: nodes render unstyled or mis-styled, and a shipped control appearing as a bare browser input reads as a design choice rather than as version skew. That is the same failure the Range control had before §1.5b, arriving across a package boundary instead of an authoring one.

`content/fuaran-reference.css` therefore carries a stamp in its header naming the class vocabulary it was written against:

```css
/* fuaran-vocabulary-fingerprint: fv1:db6e4135e0aa5b83 */
```

and the renderer exposes the matching value, so the host can assert the two agree.

**What the fingerprint covers.** The **class vocabulary** — exactly the enumeration §1.5b's coverage suite builds, which is the Theme projections run over every DU case unioned with the structural class literals scanned out of the renderer sources. It moves when a class enters or leaves the vocabulary.

**What it deliberately does not cover.** The **rules**. Re-colouring `.fuaran-callout`, retuning the token defaults, or adding a media query leaves it unchanged. It answers *does this sheet know the classes this renderer emits* — the skew that silently breaks a control — and nothing else. Whole-sheet identity is a different question with a different answer already: the sha256 printed by `Build.fsproj -- CssCheck`, and the byte-copy assertion in §1.5c. A host that needs byte identity should hash against the packaged copy rather than read the fingerprint as if it meant that.

**The recommended host assertion.** Do it once at startup, and fail hard — the whole value is that a mismatch stops a deploy instead of producing a bug report about a page looking wrong:

```fsharp
open Fuaran.UI.Renderer.Server

match Render.checkStylesheet (File.ReadAllText servedStylesheetPath) with
| Ok () -> ()
| Error message -> failwith message   // do not degrade to a warning
```

`Render.vocabularyFingerprint` is the constant if you would rather compare it yourself, and `Render.stylesheetFingerprint` reads the stamp out of a stylesheet's text. All three take or return plain values and read no files: where a host's stylesheet comes from — a static file, an embedded resource, something fetched at boot — is the host's business, and a check that guessed would be checking the wrong bytes.

Two boundaries worth stating, because both are decisions rather than omissions. An **unstamped** sheet is an `Error`, not a pass: a host that calls this has asserted the served sheet is the packaged one, and staying silent about a sheet that cannot be identified is precisely the outcome the check removes. And a host serving **its own replacement sheet** (the §4l down-shift path) should not call this at all — it implements the class hooks in Sections 2–4 directly, and the fingerprint of the reference sheet says nothing about whether it did.

**The value is machine-maintained, in three links.** `Theme.vocabularyFingerprint` is pinned rather than computed, because half the vocabulary is read out of the renderer *sources*, which a shipping package cannot see at runtime. So: the coverage suite recomputes the truth and fails naming the value to pin when the vocabulary moves; `-- CssCheck` (wired into `Check`) fails when the stylesheet's stamp and the constant disagree; and the byte-copy check carries the stamp to every tier. Changing the vocabulary is therefore: run the suite, paste the value it names into `Theme.fs`, run `-- Css`, commit the sheet and its three tier copies in the same change-set. No step of that is remembered rather than enforced.

### 1.6 Interaction state matrix (Phase 12.N) – 84 + 4 = 88 variables

The static palette in §1.1 describes the **idle** appearance of every tone. The interaction matrix adds per-state × per-tone × per-slot tokens so consumers can theme `:hover` / `:focus-visible` / `:active` / `:disabled` independently of the base palette, without monkey-patching `.fuaran-button` / `.fuaran-tab` / `.fuaran-callout-dismiss` etc. Pre-12.N every interactive surface inherited an opinionated `filter: brightness(0.92)` hover with no theme escape hatch; post-12.N the brightness opinion is gone and tokens are the only knob.

Variable name shape: `--fuaran-tone-{tone}-{state}-{slot}` where the axes are:

- `tone ∈ {default, subdued, brand, success, warning, critical, info}` (7) – matches §1.1.
- `state ∈ {hover, focus, active, disabled}` (4) – `:focus-visible`-bound, not `:focus`.
- `slot ∈ {bg, fg, border}` (3) – matches the §1.1 slot vocabulary.

Total: 7 × 4 × 3 = **84 tone-state-slot variables**, listed in stable order in the reference CSS `:root` block (`fuaran-reference.css` post-12.N) grouped by state then by tone.

Plus **4 focus-ring globals** controlling the `:focus-visible` outline shape on every interactive surface:

| Variable | Reference value | Notes |
|---|---|---|
| `--fuaran-focus-ring-color` | `#93c5fd` (brand-border) | drives `outline-color` on every `:focus-visible` rule |
| `--fuaran-focus-ring-width` | `2px` | drives `outline-width` |
| `--fuaran-focus-ring-offset` | `2px` | drives `outline-offset` |
| `--fuaran-focus-ring-style` | `solid` | drives `outline-style` |

#### 1.6.1 Static fallback rules

The reference CSS picks tokens such that the default values map semantically to a small set of rules (consumers can rely on this):

- **Hover** – each slot defaults to a darker variant of the base tone's same slot (one Tailwind stop down on the palette ladder).
- **Focus** – `bg` / `fg` default to the base value (no surface shift on focus – the outline ring is the primary affordance); `border` defaults to brand-border so a focused-edge tint reads as a brand accent.
- **Active** – each slot defaults to a darker-than-hover variant.
- **Disabled** – every tone's slot collapses to the matching subdued slot (`bg: #f3f4f6`, `fg: #6b7280`, `border: #d1d5db`). This is the "drained colour" convention.

#### 1.6.2 Per-component state surface

Which states the reference CSS actually applies to each interactive class:

| Surface | hover | focus-visible | active | disabled |
|---|---|---|---|---|
| `.fuaran-button-primary` | ✅ tone-brand-hover-fg | ✅ outline ring | ✅ tone-brand-active-fg | ✅ tone-brand-disabled-fg + opacity |
| `.fuaran-button-secondary` | ✅ tone-default-hover-bg + border | ✅ outline ring | ✅ tone-default-active-bg + border | ✅ tone-default-disabled-{bg,fg,border} |
| `.fuaran-button-tertiary` | ✅ tone-brand-hover-bg | ✅ outline ring | ✅ tone-brand-active-bg | ✅ tone-brand-disabled-fg (text only) |
| `.fuaran-button-destructive` | ✅ tone-critical-hover-fg | ✅ outline ring | ✅ tone-critical-active-fg | ✅ tone-critical-disabled-fg + opacity |
| `.fuaran-tab` | ✅ tone-brand-hover-{fg,border} | ✅ outline ring | – | – |
| `.fuaran-stepper-step` | ✅ tone-subdued-hover-{bg,fg} | – | – | – |
| `.fuaran-callout-dismiss` | ✅ opacity shift | ✅ outline ring | – | – |
| `.fuaran-grid-row` / `.fuaran-table-row` | ✅ tone-subdued-hover-bg | – | – | – |
| `.fuaran-form-input` / `.fuaran-form-select` / `.fuaran-form-textarea` | – | ✅ outline ring + tone-default-focus-border | – | – |
| `.fuaran-filter-input` / `.fuaran-filter-select` | – | ✅ outline ring + tone-default-focus-border | – | – |
| `.fuaran-select-control` | – | ✅ outline ring + tone-default-focus-border | – | – |
| `.fuaran-grid-cell-editable` | – | ✅ outline ring + tone-default-focus-border | – | – |
| `.fuaran-grid-cell-button` | ✅ tone-default-hover-{bg,border} | ✅ outline ring | – | – |
| `.fuaran-form-submit` | ✅ tone-brand-hover-fg | ✅ outline ring | ✅ tone-brand-active-fg | – |

The matrix records which states the reference CSS exercises; the variable surface is fully populated regardless (all 84 + 4 are declared at `:root`), so consumers who add their own rules – e.g. `:hover` on `.fuaran-metric` or `:active` on `.fuaran-callout` – can consume the tokens without needing to author the variables.

#### 1.6.3 Anti-patterns (extending §6)

- **Don't drop the `:focus-visible` distinction in favour of bare `:focus`.** The reference CSS deliberately uses `:focus-visible` so keyboard navigation gets the outline ring but mouse clicks don't. Consumers who want both can layer their own `:focus { ... }` rule on top; consumers who want only-keyboard behaviour get it by default.
- **Don't `outline: none` interactive elements at the base layer.** The reference declares the outline ring through `:focus-visible` so removing it at the base layer would create an unstyled-focus moment. If a host design demands no outlines, override `--fuaran-focus-ring-width: 0;` instead – the outline declaration still emits, but its width collapses to zero.
- **Don't bind the per-state tokens as `color-mix()` of the base tones.** The reference uses static hex values for cross-browser consistency (the phase body flagged sRGB-vs-OKLCH drift). If a consumer wants `color-mix()`-derived state colours, they can re-bind individual variables; the contract is that the variable holds a CSS colour expression, not that it's a static hex.
- **Don't apply per-state tokens to non-interactive surfaces** (`.fuaran-metric`, `.fuaran-callout`, `.fuaran-badge`). The variable surface includes the passive tones for completeness (a consumer might author hover-able variants), but the reference CSS does not apply them – pre-12.N convention preserved.

## 2. Class hooks the renderer emits

Every Fuaran node renders inside a wrapper element whose `className` is computed by [`Theme.nodeClassName`](src/Fuaran.UI.Renderer/Theme.fs:110). That string is the concatenation of:

1. `fuaran-kind-{...}` – per-`NodeKind` tag (Section 2.1).
2. `fuaran-node fuaran-tone-{...} fuaran-weight-{...} fuaran-emphasis-{...}` – semantic-style tokens (Section 2.0).

Inner kind-specific class hooks are emitted by the per-kind renderers (Section 3+).

### 2.0 SemanticStyle hooks (always present on outer wrapper)

| Class | Source | Notes |
|---|---|---|
| `fuaran-node` | every node | base hook; consumers MUST NOT override (see anti-patterns) |
| `fuaran-tone-{default,subdued,brand,success,warning,critical,info}` | `style.Tone` | 7 variants |
| `fuaran-weight-{compact,standard,spacious}` | `style.Weight` | 3 variants – consumer hook, deliberately unstyled by the reference sheet (§1.5) |
| `fuaran-emphasis-{quiet,normal,loud}` | `style.Emphasis` | 3 variants – consumer hook, deliberately unstyled by the reference sheet (§1.5) |

### 2.1 Per-`NodeKind` base hooks (25 variants)

One class per `NodeKind<'Msg>` case ([`Theme.kindClass`](src/Fuaran.UI.Renderer/Theme.fs:76)):

| `NodeKind` case | Emitted class | Notes |
|---|---|---|
| `Layout.Dashboard` | `fuaran-kind-dashboard` | |
| `Layout.Stack` | `fuaran-kind-stack` | |
| `Layout.GridLayout` | `fuaran-kind-grid-layout` | distinct from `fuaran-kind-grid` (Visualisation) |
| `Layout.SplitPanel` | `fuaran-kind-split-panel` | |
| `Layout.Tabs` | `fuaran-kind-tabs` | |
| `Layout.Card` | `fuaran-kind-card` | |
| `Layout.Stepper` | `fuaran-kind-stepper` | |
| `Layout.SummaryList` | `fuaran-kind-summary-list` | Phase 12.P – single-card-of-rows shape |
| `Display.Heading` | `fuaran-kind-heading` | |
| `Display.LabelValueRow` | `fuaran-kind-label-value-row` | Phase 12.P – single label-left / value-right row |
| `Display.Markdown` | `fuaran-kind-markdown` | |
| `Display.Metric` | `fuaran-kind-metric` | |
| `Display.Badge` | `fuaran-kind-badge` | |
| `Display.Sparkline` | `fuaran-kind-sparkline` | |
| `Display.Spacer` | `fuaran-kind-spacer` | |
| `Display.Callout` | `fuaran-kind-callout` | |
| `Display.Progress` | `fuaran-kind-progress` | |
| `Display.Skeleton` | `fuaran-kind-skeleton` | |
| `Input.Form` | `fuaran-kind-form` | |
| `Input.Filters` | `fuaran-kind-filters` | |
| `Input.Button` | `fuaran-kind-button` | |
| `Input.FileUpload` | `fuaran-kind-file-upload` | |
| `Input.Select` | `fuaran-kind-select` | |
| `Visualisation.DataGrid` | `fuaran-kind-grid` | distinct from `fuaran-kind-grid-layout` (Layout) |
| `Visualisation.Chart` | `fuaran-kind-chart` | |
| `Visualisation.Table` | `fuaran-kind-table` | |
| `Visualisation.Map` | `fuaran-kind-map` | |
| `Custom(moduleId, componentId, _)` | `fuaran-kind-custom fuaran-custom-{moduleId}-{componentId}` | both segments sanitised – see Section 5 |

## 3. Dynamic class suffixes (the Tailwind-safelist list)

Classes whose suffix is computed at render-time from spec / state. Tailwind JIT can't see these without explicit safelisting; the list below is the canonical safelist source.

### 3.1 Layout-side dynamic suffixes

| Class | Source |
|---|---|
| `fuaran-stack-vertical` / `fuaran-stack-horizontal` | `StackSpec.Orientation` |
| `fuaran-stack-wrap` | added when `StackSpec.Wrap = true` (Phase 12.P) |
| `fuaran-tabs-horizontal` / `fuaran-tabs-vertical` | `TabsSpec.Orientation` |
| `fuaran-tab-active` | added to selected tab in `Tabs` |
| `fuaran-stepper-step-active` | added to current step in `Stepper` |
| `fuaran-split-pane-left` / `fuaran-split-pane-right` | first / second child in `SplitPanel` |

### 3.2 Display-side dynamic suffixes

| Class | Source |
|---|---|
| `fuaran-spacer-small` / `fuaran-spacer-medium` / `fuaran-spacer-large` | `SpacerSpec.Size` |
| `fuaran-progress-indeterminate` | added when `ProgressSpec.Indeterminate = true` |
| `fuaran-sparkline-empty` | added to sparkline when source resolves to empty |
| `fuaran-heading-eyebrow` / `fuaran-heading-caption` / `fuaran-heading-lead` | `HeadingSpec.Variant` (Phase 12.P – Standard emits no suffix) |
| `fuaran-label-value-row-emphasis` | added when `LabelValueRowSpec.Emphasis = true` (Phase 12.P) |

### 3.2a Motion suffixes (Phase 12.F – outer-wrapper)

The renderer emits one `fuaran-motion-{token}` class on the outer wrapper when `Node.Motion = Some token`. Eight tokens; four ship with `@keyframes` in the reference CSS, four are no-op class hooks for consumer extension.

| Class | Source | Reference CSS rule |
|---|---|---|
| `fuaran-motion-none` | `Motion.None` | no-op |
| `fuaran-motion-pulse-during-load` | `Motion.PulseDuringLoad` | `@keyframes fuaran-motion-pulse` – opacity 1 → 0.5 → 1 |
| `fuaran-motion-fade-in-on-mount` | `Motion.FadeInOnMount` | `@keyframes fuaran-motion-fade-in` – opacity 0 → 1 |
| `fuaran-motion-slide-in-from-below` | `Motion.SlideInFromBelow` | no-op |
| `fuaran-motion-shake-on-error` | `Motion.ShakeOnError` | `@keyframes fuaran-motion-shake` – ±4px translateX |
| `fuaran-motion-rotate-on-refresh` | `Motion.RotateOnRefresh` | no-op |
| `fuaran-motion-slide-in-from-right` | `Motion.SlideInFromRight` | `@keyframes fuaran-motion-slide-in-right` – translateX(16px) + fade |
| `fuaran-motion-expand-collapse` | `Motion.ExpandCollapse` | no-op |

`@media (prefers-reduced-motion: reduce)` disables every shipped keyframe rule. Consumers authoring overrides for the no-op hooks should respect the same media query.

### 3.3 Input-side dynamic suffixes

| Class | Source |
|---|---|
| `fuaran-button-unwired` | added to buttons whose `OnClick` action transitively contains a non-Dispatch / non-Chain branch (Call / Notify / Navigate / SetState / AiTool) – Phase 12 session 3b convention to mark substrate-routed actions visually |

### 3.5 The uniform icon hook

Every icon-bearing spec (tab header / Fact / Metric / Callout / Button) renders its `IconSource` as one EMPTY placement element – `<span class="fuaran-icon fuaran-{kind}-icon" data-icon="{name}" aria-hidden="true"></span>`. The icon name rides `data-icon`, never the text content; the reference CSS ships no glyphs, so with no host icon system the hook renders as nothing. Map `data-icon` to glyphs with your own mechanism, e.g. `.fuaran-icon[data-icon="user"]::before { content: ...; }`, an icon-font class added by hydration, or SVG injection.

| Class | Emitted on |
|---|---|
| `fuaran-icon` | every icon hook (shared base) |
| `fuaran-tab-icon` | a tab header with `Icon` set |
| `fuaran-fact-icon` | a Fact with `Icon` set (inside `.fuaran-fact-value`) |
| `fuaran-metric-icon` | a Metric with `Icon` set (leads the tile) |
| `fuaran-callout-icon` | a Callout with `Icon` set (leads the column) |
| `fuaran-button-icon` | a Button with `Icon` set (leads the label) |

### 3.4 Visualisation-side dynamic suffixes

| Class | Source |
|---|---|
| `fuaran-skeleton-row` | one per row in `SkeletonSpec.Rows` |
| `fuaran-grid-row` / `fuaran-table-row` | every data row (clickable when `OnRowClick` is wired) |

## 4. Per-spec tone / variant suffixes

The per-spec variant DUs project to class suffixes. The renderer's `Tone` / `Variant` propagation is the only way a host stylesheet can colour a spec independently from the outer `fuaran-tone-*` wrapper.

### 4.1 Tone-bearing components (4 × 7 = 28 classes)

The full 7×N matrix landed in Phase 12.H. Pre-12.H several tones rendered the default styling because their CSS rules were missing – Phase 12.H closed this gap by completing the matrix in `fuaran-reference.css` AND propagating `MetricSpec.Tone` through `renderMetric` (which previously dropped the tone on the floor).

| Component | Default | Subdued | Brand | Success | Warning | Critical | Info |
|---|---|---|---|---|---|---|---|
| Callout | `fuaran-callout-default` | `fuaran-callout-subdued` | `fuaran-callout-brand` | `fuaran-callout-success` | `fuaran-callout-warning` | `fuaran-callout-critical` | `fuaran-callout-info` |
| Progress | `fuaran-progress-default` | `fuaran-progress-subdued` | `fuaran-progress-brand` | `fuaran-progress-success` | `fuaran-progress-warning` | `fuaran-progress-critical` | `fuaran-progress-info` |
| Pill (grid cell) | `fuaran-pill-default` | `fuaran-pill-subdued` | `fuaran-pill-brand` | `fuaran-pill-success` | `fuaran-pill-warning` | `fuaran-pill-critical` | `fuaran-pill-info` |
| Metric | `fuaran-metric-default` | `fuaran-metric-subdued` | `fuaran-metric-brand` | `fuaran-metric-success` | `fuaran-metric-warning` | `fuaran-metric-critical` | `fuaran-metric-info` |

The tone suffix uses `Theme.toneVar` – the same function the outer wrapper uses for `fuaran-tone-*` – so the two classes always match for the same `ToneVariant` value.

### 4.2 Badge variants (6 classes)

`BadgeVariant` is its own DU, distinct from `ToneVariant`. See Section 4.4 for the vocabulary fork.

| Variant | Class |
|---|---|
| `Neutral` | `fuaran-badge-neutral` |
| `Brand` | `fuaran-badge-brand` |
| `Success` | `fuaran-badge-success` |
| `Warning` | `fuaran-badge-warning` |
| `Critical` | `fuaran-badge-critical` |
| `Info` | `fuaran-badge-info` |

### 4.3 Button variants (4 classes + 1 modifier)

| Variant | Class |
|---|---|
| `Primary` | `fuaran-button-primary` |
| `Secondary` | `fuaran-button-secondary` |
| `Tertiary` | `fuaran-button-tertiary` |
| `Destructive` | `fuaran-button-destructive` |
| (modifier) | `fuaran-button-unwired` (see Section 3.3) |

### 4.4 `BadgeVariant` ↔ `ToneVariant` vocabulary fork

The two DUs are intentionally separate. `BadgeVariant` is the in-band "what semantic flavour is this badge" surface; `ToneVariant` is the styling-token surface used by every other tone-bearing component. The fork is documented in the Fuaran design specification §4k Q3.4 – the merge would ripple through the AI authoring guide.

Side-by-side mapping (so consumers stop expecting `fuaran-tone-neutral` to exist):

| `BadgeVariant` | Closest `ToneVariant` | Notes |
|---|---|---|
| `Neutral` | `Subdued` | NOT `Default`. Badges historically used a flat grey-on-grey; `Subdued` matches it. |
| `Brand` | `Brand` | direct |
| `Success` | `Success` | direct |
| `Warning` | `Warning` | direct |
| `Critical` | `Critical` | direct |
| `Info` | `Info` | direct |

Consumers who want the `Neutral` badge to share styling with `fuaran-tone-subdued-bg` should style `.fuaran-badge-neutral` against `var(--fuaran-tone-subdued-bg)` – not invent a `--fuaran-tone-neutral-*` variable. The reference CSS does exactly this.

## 5. Reserved class fragments (Custom nodes)

`NodeKind.Custom(moduleId, componentId, props)` carries unconstrained-string `moduleId` and `componentId`. The renderer projects each through `Theme.sanitiseClassFragment` ([`Theme.fs:32`](src/Fuaran.UI.Renderer/Theme.fs)) which replaces every character outside `[a-zA-Z0-9_-]` with `-` before interpolation into the emitted `fuaran-custom-{moduleId}-{componentId}` class.

**Implications for hosts:**

- A `Custom("My Module", "Pill Chart", _)` node emits `fuaran-custom-My-Module-Pill-Chart` – DO NOT assume the raw IDs survive verbatim.
- Two distinct Custom nodes that differ only in characters the sanitiser collapses (e.g. `"my.module"` and `"my-module"`) collide on the same class. Pick module/component IDs that are already CSS-identifier-safe to avoid surprise.
- The `fuaran-kind-custom` base class is always present on the outer wrapper, so styling-by-class against `fuaran-kind-custom { ... }` catches every Custom node regardless of sanitisation.

### 5.1 The `IFuaranRuntime.TryRenderCustom` runtime hook (Phase 12.F)

Pre-12.F, the renderer always emitted the labelled-placeholder fallback for every Custom node. Phase 12.F gives `IFuaranRuntime` a new abstract member so hosts can register real renderers per `(moduleId, componentId)`:

```fsharp
abstract TryRenderCustom :
    moduleId : string * componentId : string * props : Map<string, JVal> -> ReactElement option
```

Renderer dispatch order:

1. `ctx.Runtime.TryRenderCustom(moduleId, componentId, props)` – registered renderer wins.
2. On `None`, emit a `<div class="fuaran-custom-placeholder">` containing the labelled body the renderer used pre-12.F.

The runtime ships two registration surfaces:

- `Fuaran.UI.Renderer.Runtime.MutableRuntime` – .NET-side (tests, Fable-compiler-not-required hosts). Diagnostic shape for the other substrate members.
- `Fuaran.UI.Renderer.BrowserRuntime` – browser-side (the default for Fable apps). Browser-shaped Call / Notify / Navigate / SetState / InvokeAiTool, plus the Custom registry.

Both expose:

```fsharp
member _.RegisterCustomRenderer
    (moduleId : string, componentId : string, renderFn : Map<string, JVal> -> ReactElement)
    : unit
```

Registration is consumer-side. AI emitting `kind: "custom"` MUST NOT assume any specific renderer is registered – that's a host concern, kept opaque from the typed-tree contract.

### 5.2 Placeholder class change (Phase 12.F)

The pre-12.F inner `<div class="fuaran-custom">` placeholder is gone. The labelled-fallback body is now wrapped in `<div class="fuaran-custom-placeholder">`, which is the class that carries the dashed-border styling. Reasons:

- The previous `.fuaran-custom` class conflicted with the per-instance `fuaran-custom-{moduleId}-{componentId}` class on the outer wrapper.
- Hosts that register a renderer via `TryRenderCustom` get their element rendered verbatim – they should not inherit the dashed-border placeholder styling. Scoping the dashed-border rule to `.fuaran-custom-placeholder` keeps the registered-renderer surface clean.
- The outer wrapper carries `data-fuaran-node-id` for every Kind including Custom, so `LayoutObserver` / `SnapshotRegistry` (Phase 12.G) now cover Custom nodes uniformly.

Consumers who styled against `.fuaran-custom` directly should switch to either `.fuaran-kind-custom` (catches every Custom node, registered or placeholder) or `.fuaran-custom-placeholder` (only the labelled fallback).

## 6. Anti-patterns

- **Don't override `.fuaran-node` base styling.** It's the universal-marker class; touching it propagates to every Fuaran node and is almost never what you want. Override at the variable layer (`--fuaran-tone-*`) or at the per-kind / per-spec hook instead.
- **Don't redefine the tone variables only at the per-component layer.** `--fuaran-tone-brand-bg` is the single source of truth; binding `--fuaran-callout-brand-bg` separately defeats the contract and means new tone-bearing components (Phase 12.K's `--fuaran-emphasis-loud-border`-style additions) won't pick up your colour.
- **Don't forget to safelist the dynamic suffixes** (Section 3) in Tailwind JIT configs. The class strings are computed at render-time; the JIT scanner won't see them. Safelist with `safelist: ['fuaran-tab-active', 'fuaran-stepper-step-active', 'fuaran-stack-vertical', 'fuaran-stack-horizontal', 'fuaran-stack-wrap', 'fuaran-tabs-horizontal', 'fuaran-tabs-vertical', 'fuaran-split-pane-left', 'fuaran-split-pane-right', { pattern: /^fuaran-spacer-(small|medium|large)$/ }, 'fuaran-skeleton-row', 'fuaran-progress-indeterminate', 'fuaran-sparkline-empty', 'fuaran-button-unwired', 'fuaran-label-value-row-emphasis', { pattern: /^fuaran-heading-(eyebrow|caption|lead)$/ }, { pattern: /^fuaran-(callout|progress|pill|metric)-(default|subdued|brand|success|warning|critical|info)$/ }, { pattern: /^fuaran-badge-(neutral|brand|success|warning|critical|info)$/ }, { pattern: /^fuaran-button-(primary|secondary|tertiary|destructive)$/ }, { pattern: /^fuaran-motion-(none|pulse-during-load|fade-in-on-mount|slide-in-from-below|shake-on-error|rotate-on-refresh|slide-in-from-right|expand-collapse)$/ }]`.
- **Don't add per-component variables like `--fuaran-button-brand-fill`.** The renderer composes tone × component via class names, not per-component variables. A `--fuaran-button-brand-fill` would double the contract surface and break the §4l down-shift portability promise (the Apache 2.0 reference CSS is the canonical class vocabulary; per-component variables would have to be re-invented for every consumer-side fork).
- **Don't ship `fuaran-reference.css` as the sole styling surface and call it done.** Reference CSS is a working default; consumers who already own design tokens (`--color-brand`, shadcn `--primary`, MUI `--mui-palette-primary-main`) bridge per [`docs/THEME-BRIDGE-GUIDE.md`](docs/THEME-BRIDGE-GUIDE.md) and let their existing design system flow through to the `--fuaran-tone-*` surface.
- **Don't drop the `var(--X, fallback)` form** when amending the reference CSS or authoring a host stylesheet that re-implements pieces of it. A consumer that forgets to wire one variable should still see something recognisable for that one node – not a silently-broken empty rule. Phase 12.H rewrote the reference CSS to use fallbacks everywhere precisely because a real consumer's pre-bridge state rendered Fuaran with ~100% styling loss when the variables were unset; fallbacks turn that into "looks like the reference theme" rather than "looks like raw markup".

## See also

- [`HOST-INTEGRATION-CHECKLIST.md`](HOST-INTEGRATION-CHECKLIST.md) – the six host-interface wires (host-integration tier).
- [`docs/THEME-BRIDGE-GUIDE.md`](docs/THEME-BRIDGE-GUIDE.md) – bridging consumer design tokens to Fuaran's variable surface (four worked examples).
- [`src/Fuaran.UI.Renderer/content/fuaran-reference.css`](src/Fuaran.UI.Renderer/content/fuaran-reference.css) – the packaged reference stylesheet (Apache 2.0).
- [`src/Fuaran.UI.Renderer/content/fuaran-bridge.css.template`](src/Fuaran.UI.Renderer/content/fuaran-bridge.css.template) – copy-and-customise alias template.
- [`docs/migrations/12-H-host-styling-contract.md`](docs/migrations/12-H-host-styling-contract.md) – Phase 12.H migration notes (before/after diff + §4l down-shift portability clarification).
- [`CLAUDE.md`](CLAUDE.md) – repo layout + conventions.
- The Fuaran design specification §4k Q3.4 – the `BadgeVariant` ↔ `ToneVariant` decision.
