# Phase 12.H – Fuaran host-styling contract + tone matrix completion

**Phase:** `Fuaran/roadmap/phases/12-H-fern-host-styling-contract.md`
**Date:** 2026-05-26
**Stability impact:** Additive – no breaking change. See "Compatibility" below.

## What changes

Phase 12.H turns Fuaran's previously-implicit class-hook contract into a documented, packaged one – parallel to what Phase 12.Y.2 did for the orchestration tier's mandatory wires (Gap #2: implicit contracts get skipped, explicit contracts get honoured).

Three artefacts land:

1. [`Fuaran/HOST-STYLING-CHECKLIST.md`](../../HOST-STYLING-CHECKLIST.md) – new workspace-level companion to `HOST-INTEGRATION-CHECKLIST.md`. Six sections: (1) CSS variables (tone palette, spacing scale, typography scale, component dimensions), (2) per-`NodeKind` base class hooks, (3) dynamic class suffixes (the Tailwind-safelist list), (4) per-spec tone / variant suffixes (the full 7×N matrix), (5) reserved class fragments (the `sanitiseClassFragment` rule for Custom nodes), (6) anti-patterns.
2. [`Fuaran/docs/THEME-BRIDGE-GUIDE.md`](../THEME-BRIDGE-GUIDE.md) + [`content/fuaran-bridge.css.template`](../../src/Fuaran.UI.Renderer/content/fuaran-bridge.css.template) – four worked examples for bridging consumer design tokens to Fuaran's `--fuaran-tone-*` surface (Tailwind with CSS-variable extensions, shadcn / Radix, raw CSS tokens, dark mode via `prefers-color-scheme`). The template is packaged with the NuGet so consumers can `copy + customise` rather than re-derive from scratch.
3. Reference stylesheet promoted to the `Fuaran.UI.Renderer` NuGet at [`content/fuaran-reference.css`](../../src/Fuaran.UI.Renderer/content/fuaran-reference.css) (Apache 2.0 licensed via header). `samples/demo/index.css` becomes a thin `@import` of the canonical file.

Three renderer / CSS edits land alongside the docs:

4. **Tone matrix completion** – `fuaran-reference.css` now has rules for the previously-missing tones: callout's `default` / `subdued` / `brand` / `info`, progress's `default` / `subdued` / `warning` / `critical` / `info`, and all 7 pill tones (none existed pre-12.H). Each rule uses `var(--fuaran-tone-{tone}-{slot}, fallback)` so an unstyled-mode renderer still shows recognisable colours (Critical-A).
5. **`KPISpec.Tone` propagation** – [`Render.fs`](../../src/Fuaran.UI.Renderer/Render.fs)'s `renderKPI` now emits `fuaran-kpi fuaran-kpi-{Theme.toneVar spec.Tone}` instead of the previously-unread `spec.Tone`. The reference CSS gains per-tone `.fuaran-kpi-{tone}` rules to match the other tone-bearing components; the older outer-scoped `.fuaran-tone-brand .fuaran-kpi` shape is replaced by direct rules for consistency.
6. **Variable surface lift (Critical-C-i)** – every hardcoded pixel in the reference CSS now references a `var(--fuaran-X, Ypx)` shape, drawn from three new variable groups: spacing scale (`--fuaran-space-{xs|sm|md|lg|xl}`), typography scale (`--fuaran-text-{xs..3xl}`, `--fuaran-font-weight-{regular..bold}`, `--fuaran-line-height-{tight..relaxed}`), and component dimensions (`--fuaran-button-pad-{y|x}`, `--fuaran-radius-{sm|md|lg|full}`, `--fuaran-border-width`). Consumers can now override density without forking the reference CSS.

Plus one doc-drift fix:

7. **`--fuaran-button-brand-fill` walkthrough drift** – the pilot-app walkthrough (maintainers' workspace docs) referenced a non-existent per-component variable. Replaced with the actual emitted shape (`--fuaran-tone-brand-bg` / `-fg`) plus a bridge-stylesheet pointer.

## Diff highlights

### Tone matrix – what was missing

Pre-12.H tone coverage in the reference CSS:

| Component | default | subdued | brand | success | warning | critical | info |
|---|---|---|---|---|---|---|---|
| callout | ❌ | ❌ | ❌ | ✅ | ✅ | ✅ | ❌ (only the fallthrough `.fuaran-callout` rule, defaulted to info colours) |
| progress | ❌ | ❌ | ✅ | ✅ | ❌ | ❌ | ❌ |
| pill | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ (zero rules – every pill rendered the default styling regardless of `tone row`) |
| kpi | ❌ | ❌ | ✅ (via `.fuaran-tone-brand .fuaran-kpi` outer-scoped) | ❌ | ❌ | ❌ | ❌ |
| badge | (uses BadgeVariant, not ToneVariant; 5/6 rules existed; Neutral missing) | | | | | | |

Post-12.H: every cell ✅. Plus the renderer's `renderKPI` now emits the matching class.

### `KPISpec.Tone` propagation diff

```fsharp
// Pre-12.H — `spec.Tone` is read but never used in the kpi-body branch:
    | _ ->
        Html.div
            [ prop.className "fuaran-kpi"
              ...

// Post-12.H:
    | _ ->
        Html.div
            [ prop.className (sprintf "fuaran-kpi fuaran-kpi-%s" (Theme.toneVar spec.Tone))
              ...
```

Pre-12.H a `Fuaran.kpi "ratio" { spec with Tone = ToneVariant.Critical }` rendered identically to one with `Tone = ToneVariant.Default` – `spec.Tone` reached `renderKPI` but the function dropped it.

### `var(--X, fallback)` rewrite

Every variable reference in `fuaran-reference.css` gained a literal-colour fallback so a consumer that forgets to bridge one variable still gets recognisable styling for that node. Pattern:

```css
/* Pre-12.H: */
.fuaran-callout-warning { color: var(--fuaran-tone-warning-fg); }

/* Post-12.H: */
.fuaran-callout-warning { color: var(--fuaran-tone-warning-fg, #b45309); }
```

This is the load-bearing change for closing the failure mode the pilot-app integration audit (2026-05-26) surfaced: the app's `index.css` defines none of the `--fuaran-*` variables, so every variable lookup pre-12.H resolved to empty + the CSS rule no-opped. Post-12.H, an unbridged consumer sees the reference colours; with a bridge file, sees the consumer's colours.

## Verification steps

1. **`dotnet pack`** in `src/Fuaran.UI.Renderer/`. Confirm the resulting `Fuaran.UI.Renderer.X.Y.Z.nupkg` contains:
   - `content/fuaran-reference.css`
   - `content/fuaran-bridge.css.template`
   - `content/README.md`
2. **`dotnet build Fuaran.sln`** clean (no warnings, per `Directory.Build.props`'s `TreatWarningsAsErrors=true`).
3. **`dotnet run --project src/Fuaran.UI.Tests`** – the new `ToneClassEmissionTests` suite passes (4 sub-test-lists covering ToneVariant suffix mapping, per-spec class shape for callout/progress/pill/kpi, BadgeVariant suffix mapping, outer-wrapper `fuaran-tone-{tone}` emission).
4. **`dotnet fable -o output --noCache`** in `samples/demo/`. Clean. Browser at the demo's Vite port renders every tone × component combination visibly distinct (manual visual check).
5. **Grep** of `--fuaran-button-` shapes outside the legitimate `--fuaran-button-pad-{x,y}` variables returns zero hits across `Fuaran/docs/**` and `Fuaran/samples/**`:
   ```bash
   rg --type md '--fuaran-button-' Fuaran/docs/ Fuaran/samples/
   ```
   Should match only the legitimate `--fuaran-button-pad-y` / `--fuaran-button-pad-x` references in `HOST-STYLING-CHECKLIST.md` (workspace root, not `docs/`) and the anti-pattern callouts in `HOST-STYLING-CHECKLIST.md` + `THEME-BRIDGE-GUIDE.md` that explicitly mention `--fuaran-button-brand-fill` as a non-existent shape to avoid.

## Compatibility

Phase 12.H is **strictly additive** – no breaking change.

- The `var(--X, fallback)` rewrite preserves existing computed values byte-for-byte. The fallback IS the value previously hardcoded, so consumers already using the reference CSS see no visual change.
- The `KPISpec.Tone` propagation ADDS `fuaran-kpi-{tone}` to the emitted `className` string. The base `fuaran-kpi` class is still present; consumer styles hooking against `.fuaran-kpi` continue to work alongside the new tone-specific rules.
- The variable-surface lift (Critical-C-i) introduces new override paths but breaks no existing one. A consumer whose host stylesheet defines none of the new `--fuaran-space-*` / `--fuaran-text-*` / `--fuaran-button-*` variables falls through to the reference's hardcoded fallbacks – same pixel values as pre-12.H.
- The NuGet `Content` packaging is additive – pre-12.H consumers who hand-copy `samples/demo/index.css` keep working.
- The new `.fuaran-kpi-default` rule has the same visual outcome as the old default `.fuaran-kpi` rule (white bg + default border), so a KPI with the default tone renders identically pre- and post-12.H.

The four missing-rule additions (`fuaran-callout-default` / `fuaran-callout-subdued` / `fuaran-callout-brand` / `fuaran-callout-info`, all `fuaran-progress-*` except brand/success, all `fuaran-pill-*`) DO change the visual rendering of nodes that previously rendered the fallback styling. That's the intended fix – pre-12.H, a `CalloutSpec { Tone = ToneVariant.Brand }` rendered with the info-coloured fallthrough rule, not brand styling. Consumers whose code relied on the pre-12.H wrong-colour rendering will see the correct colour after upgrading; this is a fix, not a regression.

## Rollback

If a consumer needs to revert visually:

1. Pin to a pre-12.H `Fuaran.UI.Renderer` NuGet version (`Fuaran.UI.Renderer < 0.X.Y` where Y is 12.H's release tag).
2. Or override the new tone rules in the consumer stylesheet:
   ```css
   /* Restore the pre-12.H fallthrough-to-info behaviour for callout: */
   .fuaran-callout-default,
   .fuaran-callout-subdued,
   .fuaran-callout-brand,
   .fuaran-callout-info {
     border-left-color: var(--fuaran-tone-info-border);
     background: var(--fuaran-tone-info-bg);
     color: var(--fuaran-tone-info-fg);
   }
   ```

In practice rollback is unlikely to be needed – the pre-12.H behaviour was a bug, not a feature.

## §4l down-shift portability clarification

Phase 12.H crystallises an aspect of the Fuaran UI language design's §4l portability story that previous phases left ambiguous.

**Restated:** the §4l promise is that a consumer can mechanically translate a Fuaran `Node<'Msg>` tree to plain Feliz and continue running on Apache 2.0 substrate, retiring the Apache-2.0-licensed `Fuaran.UI.Renderer` package. What §4l does NOT promise is *visual* portability against the consumer's own design system – the down-shift produces *Fuaran-styled Feliz*, which re-uses the `fuaran-*` class vocabulary.

For visual portability the consumer must keep the `content/fuaran-reference.css` (or an equivalent re-implementation against the documented class hooks in `HOST-STYLING-CHECKLIST.md`). Both options remain available indefinitely:

- The packaged `fuaran-reference.css` is Apache 2.0 – explicitly via the header comment in the file itself – so a consumer can retain and modify it indefinitely – as can the rest of the renderer, which is also Apache 2.0.
- The class-hook contract (`HOST-STYLING-CHECKLIST.md`) is documentation, not code – there is no licensing constraint on referencing it from a consumer-side re-implementation.

The **pilot-app integration is the canonical worked example** of why this clarification matters:
- **Pre-12.H state:** the app's `index.css` defined no `--fuaran-*` variables and did not import the demo CSS. Every Fuaran-rendered region rendered with ~100% styling loss – the CSS-variable-only emission resolved to empty values and the rules no-opped.
- **Post-12.H state with bridge:** the app ships a `fuaran-bridge.css` (per `THEME-BRIDGE-GUIDE.md` Example A) that re-binds `--fuaran-tone-*` to its existing `--color-brand` / `--color-success` / etc. token set. The reference stylesheet loads first; the bridge loads second; Fuaran renders against the host's design system without forking either file.
- **Post-12.H state without bridge (unstyled-mode):** the app could drop the bridge altogether and the reference CSS's `var(--X, fallback)` literals would render the reference theme – fail-soft, not silently broken.

The §4l portability story is the upper bound on stress-testing: a consumer who wants to drop the renderer entirely re-implements `Render.fs` against Feliz and the documented class vocabulary, keeps the Apache 2.0 reference CSS, and continues to render visually identically with no vendor-side runtime dependency. Phase 12.H makes both halves (class vocabulary + reference CSS) explicit + packaged + Apache-licensed; pre-12.H both halves existed only in the demo project + tribal knowledge.

## See also

- [`HOST-STYLING-CHECKLIST.md`](../../HOST-STYLING-CHECKLIST.md) – the contract.
- [`THEME-BRIDGE-GUIDE.md`](../THEME-BRIDGE-GUIDE.md) – the bridging patterns.
- [`content/fuaran-reference.css`](../../src/Fuaran.UI.Renderer/content/fuaran-reference.css) – the reference implementation.
- [`content/README.md`](../../src/Fuaran.UI.Renderer/content/README.md) – packaging notes.
- [`HOST-INTEGRATION-CHECKLIST.md`](../../HOST-INTEGRATION-CHECKLIST.md) – the orchestration-tier sibling.
- Phase 12.Y.2 – the Gap #2 "implicit contracts get skipped" framing this phase mirrors on the styling side.
- `Fuaran/roadmap/phases/12-K-fern-theme-as-api.md` – the typed-`Theme`-record follow-on. 12.K migrates the variable surface into an F# record + `Theme.toCss` emitter; this checklist remains the canonical "what class hooks does the renderer emit" source.
