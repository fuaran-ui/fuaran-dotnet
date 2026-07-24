# Phase 12.N – Fuaran interaction-state token surface

**Phase:** `Fuaran/roadmap/phases/12-N-fern-interaction-state-token-surface.md`
**Date:** 2026-05-26
**Stability impact:** Additive – pre-1.0 minor add per [`STABILITY.md`](../../STABILITY.md). No breaking change to the `Fuaran.UI` smart-constructor surface; new records are appended to `Fuaran.UI/Types.fs`; `Theme.toCss` emits additional variables alongside the pre-existing ones.

## What changes

Phase 12.N closes the "Critical-D" gap from the pilot-app visual-parity audit: pre-12.N every interactive Fuaran surface (button, tab, dismiss, form input, grid row) inherited an opinionated `filter: brightness(0.92)` hover and an unconfigurable focus outline. There was no `--fuaran-tone-brand-hover-bg`, no `--fuaran-focus-ring-color` – consumers who already owned a hover colour for `--color-brand` had no Fuaran-emit equivalent and were forced to monkey-patch `.fuaran-button` directly, defeating the AI-emit-shape promise of [`HOST-STYLING-CHECKLIST.md`](../../HOST-STYLING-CHECKLIST.md).

Post-12.N the `:hover` / `:focus-visible` / `:active` / `:disabled` cascade routes through 88 new `:root` variables consumers can re-bind at the app shell:

- **84 per-state × per-tone × per-slot tokens** (`--fuaran-tone-{tone}-{state}-{slot}`, axes `tone ∈ {default, subdued, brand, success, warning, critical, info}` × `state ∈ {hover, focus, active, disabled}` × `slot ∈ {bg, fg, border}`).
- **4 focus-ring globals** (`--fuaran-focus-ring-{color, width, offset, style}`).

The typed `Theme` record (Phase 12.K) gains an additive `Interaction: Interaction` field carrying the typed-side mirror; `Theme.toCss Defaults.theme` projects the byte-for-byte mirror of the new `:root` declarations, so consumers on the typed-Theme path get the same surface without touching CSS.

Six artefacts land:

1. **`Interaction` + `ToneStateMatrix` + `FocusRing`** records in [`src/Fuaran.UI/Types.fs`](../../src/Fuaran.UI/Types.fs), plus the additive `Interaction: Interaction` field on `Theme`.
2. **`Defaults.interaction`** (+ `Defaults.focusRing` + `Defaults.toneStateMatrix{Hover, Focus, Active, Disabled}`) in [`src/Fuaran.UI/Defaults.fs`](../../src/Fuaran.UI/Defaults.fs) – byte-for-byte mirror of the post-12.N `fuaran-reference.css` declarations.
3. **88 `:root` declarations + ~30 CSS rules** in [`src/Fuaran.UI.Renderer/content/fuaran-reference.css`](../../src/Fuaran.UI.Renderer/content/fuaran-reference.css) covering buttons (4 variants × 4 states), tab hover + focus, stepper-step hover, callout-dismiss hover + focus, grid/table row hover, form inputs focus-visible, grid-cell-editable focus-visible, grid-cell-button hover + focus, form-submit hover + focus + active.
4. **Variable-emission extension** in [`src/Fuaran.UI.Renderer/Theme.fs`](../../src/Fuaran.UI.Renderer/Theme.fs) – `Theme.toCssVariables` emits the additional 88 declarations; `Theme.toJson` / `Theme.fromJson` round-trip the new `Interaction` field.
5. **`HOST-STYLING-CHECKLIST.md` §1.6 – Interaction state matrix** ([`HOST-STYLING-CHECKLIST.md`](../../HOST-STYLING-CHECKLIST.md)) – the canonical contract: variable inventory, static fallback rules, per-component state surface table, three new anti-patterns.
6. **`STABILITY.md` entries** ([`STABILITY.md`](../../STABILITY.md)) – `Interaction` / `ToneStateMatrix` / `FocusRing` join the §4b record contract; two new rows in the worked-example table cover "add a field to `Theme`" and "add a `--fuaran-*` declaration to `fuaran-reference.css`" both as **Minor** changes.

## Diff highlights

### New types – `Fuaran.UI/Types.fs`

```fsharp
type ToneStateMatrix =
    { Default: ToneStops; Subdued: ToneStops; Brand: ToneStops
      Success: ToneStops; Warning: ToneStops; Critical: ToneStops; Info: ToneStops }

type FocusRing =
    { Color: ColorVar; Width: string; Offset: string; Style: string }

type Interaction =
    { FocusRing: FocusRing
      Hover: ToneStateMatrix
      Focus: ToneStateMatrix
      Active: ToneStateMatrix
      Disabled: ToneStateMatrix }

type Theme =
    { ...
      Interaction: Interaction }  // additive — Phase 12.N
```

`ToneStateMatrix` has the same shape as `Tones` but is a distinct nominal type so the typed surface reads "a 7-tone matrix of state slots", not "the static palette". The JSON wire shape uses the same per-tone-key vocabulary (`"default"`, `"subdued"`, etc.) for consistency with the static side.

### New value – `Fuaran.UI/Defaults.fs`

```fsharp
let interaction: Interaction =
    { FocusRing = focusRing
      Hover = toneStateMatrixHover
      Focus = toneStateMatrixFocus
      Active = toneStateMatrixActive
      Disabled = toneStateMatrixDisabled }

let theme: Theme =
    { ...
      Interaction = interaction }
```

Every hex value mirrors a matching `--fuaran-tone-{tone}-{state}-{slot}` or `--fuaran-focus-ring-*` declaration in `fuaran-reference.css`. The `ThemeTests.fs` byte-for-byte regression pins both sides – a stray edit on either side fails the variable-count or value-diff assertion.

### Reference CSS additions

`fuaran-reference.css` gains 88 declarations in `:root` plus ~30 rules across the interactive surface set listed in §1.6.2 of the checklist. Pre-12.N rules that previously applied `filter: brightness(0.92)` (button-primary hover) or hard-coded fallback hex values (tab hover, grid-row hover) now consume the new tone-state tokens with the pre-12.N visual as their inline `var(--X, <hex>)` fallback – so unstyled-mode rendering is visually equivalent to pre-12.N.

## Consumer bridge pattern

The four worked examples below cover the common host shapes a consumer might bring to Fuaran.

### Example 1 – Tailwind utility bridge

A consumer whose design system is Tailwind-shaped already has `--color-brand: theme('colors.indigo.600')` and `--color-brand-dark: theme('colors.indigo.700')` defined in their `tailwind.config.js`. Bridging the interaction tokens is a flat re-bind:

```css
/* consumer-app/src/index.css */
@layer base {
  :root {
    /* Static palette (pre-12.H bridge — already in place) */
    --fuaran-tone-brand-bg: var(--color-brand-50);
    --fuaran-tone-brand-fg: var(--color-brand);
    --fuaran-tone-brand-border: var(--color-brand-200);

    /* Interaction matrix (Phase 12.N) */
    --fuaran-tone-brand-hover-bg: var(--color-brand-100);
    --fuaran-tone-brand-hover-fg: var(--color-brand-dark);
    --fuaran-tone-brand-hover-border: var(--color-brand-300);

    --fuaran-tone-brand-active-bg: var(--color-brand-200);
    --fuaran-tone-brand-active-fg: theme('colors.indigo.800');
    --fuaran-tone-brand-active-border: theme('colors.indigo.500');

    /* Focus ring picks up the consumer's existing focus colour */
    --fuaran-focus-ring-color: var(--color-brand);
  }
}
```

Brand buttons now hover to the consumer's existing `indigo-700` rather than Fuaran's default `#1e40af`, and the focus ring matches the consumer's wider design language.

### Example 2 – shadcn `data-state="hover"` bridge

A consumer using shadcn-ui's component primitives carries `data-state` attributes for hover/active rather than relying on `:hover`. The bridge stylesheet maps the consumer-driven state attributes onto Fuaran's variable surface:

```css
/* consumer-app/src/styles/fuaran-shadcn-bridge.css */

:root {
  --fuaran-tone-brand-hover-bg: hsl(var(--primary) / 0.9);
  --fuaran-tone-brand-hover-fg: hsl(var(--primary-foreground));
  --fuaran-tone-brand-active-bg: hsl(var(--primary) / 0.8);
  --fuaran-focus-ring-color: hsl(var(--ring));
  --fuaran-focus-ring-width: 2px;
  --fuaran-focus-ring-offset: 2px;
}

/* When the consumer's component tree marks an element as hovered via
   data-state, mirror the state colours onto the Fuaran container so any
   Fuaran-rendered child picks up the same hover treatment.  */
[data-state="hover"] .fuaran-button-primary {
  background: var(--fuaran-tone-brand-hover-fg);
}
```

### Example 3 – Raw-CSS bridge (no design system)

A consumer with no design system layer can override individual variables directly at the app shell:

```css
/* consumer-app/src/styles/fuaran-overrides.css */

:root {
  /* Switch the focus ring to a thicker, brand-coloured outline */
  --fuaran-focus-ring-color: #ff6b35;
  --fuaran-focus-ring-width: 3px;
  --fuaran-focus-ring-offset: 1px;

  /* Tighten the disabled state — full opacity, dark grey, no fade */
  --fuaran-tone-brand-disabled-bg: #2a2a2a;
  --fuaran-tone-brand-disabled-fg: #888888;
  --fuaran-tone-brand-disabled-border: #333333;
}

/* If the consumer wants to disable Fuaran's opacity-fade on disabled
   buttons (the reference CSS uses opacity: 0.6 on filled-button
   disabled states), override per-component:  */
.fuaran-button-primary:disabled,
.fuaran-button-destructive:disabled {
  opacity: 1;
}
```

### Example 4 – Dark-mode hover-state inversion

A dark-mode host wants hover states to *lighten* (toward white) rather than darken, since the base tones are already dark. The override pattern is the same – re-bind the four states' tokens with inverted shifts:

```css
@media (prefers-color-scheme: dark) {
  :root {
    /* Base palette is already dark (consumer's existing dark-mode bridge) */
    --fuaran-tone-brand-bg: #1e3a8a;
    --fuaran-tone-brand-fg: #93c5fd;
    --fuaran-tone-brand-border: #3b82f6;

    /* Hover lightens instead of darkening — Tailwind one stop up */
    --fuaran-tone-brand-hover-bg: #1e40af;       /* slightly lighter than -bg */
    --fuaran-tone-brand-hover-fg: #bfdbfe;       /* lighter foreground */
    --fuaran-tone-brand-hover-border: #60a5fa;

    /* Active goes further toward white */
    --fuaran-tone-brand-active-bg: #2563eb;
    --fuaran-tone-brand-active-fg: #dbeafe;
    --fuaran-tone-brand-active-border: #93c5fd;

    /* Disabled stays muted — fg drops contrast, bg keeps base */
    --fuaran-tone-brand-disabled-bg: #1e293b;
    --fuaran-tone-brand-disabled-fg: #475569;
    --fuaran-tone-brand-disabled-border: #334155;

    /* Focus ring contrasts against dark background */
    --fuaran-focus-ring-color: #93c5fd;
  }
}
```

Or – for consumers on the typed `Theme` path – pass a dark `Interaction` value via the typed surface; the renderer emits the matching CSS bundle automatically. See [`samples/themes/Dark.fs`](../../samples/themes/Dark.fs) for the analogous static-palette pattern.

## Stability classification

Per [`STABILITY.md`](../../STABILITY.md):

- `Interaction`, `ToneStateMatrix`, `FocusRing` join the §4b record contract for `Fuaran.UI`.
- `Defaults.interaction`, `Defaults.focusRing`, `Defaults.toneStateMatrix{Hover,Focus,Active,Disabled}` join the `Defaults.X` field set for `Fuaran.UI`.
- The additive `Interaction` field on `Theme` is a pre-1.0 minor add – consumers composing `{ Defaults.theme with ... }` inherit it automatically.
- `Theme.toCssVariables` and `Theme.toJson` / `Theme.fromJson` cover the new field by extension; the public signatures don't change.
- The 88 `--fuaran-tone-{tone}-{state}-{slot}` + `--fuaran-focus-ring-*` declarations join the documented `fuaran-reference.css` variable surface (the contract `HOST-STYLING-CHECKLIST.md` §1.6 enumerates).

All additions. Pre-1.0 means consumers should still pin to the exact patch.

## Rollback

If a consumer's existing `:hover` rules conflict with the new tokens after upgrading:

1. Re-bind the affected variables at the app-shell layer to the consumer's pre-12.N expected values, or
2. Author higher-specificity rules that override the reference cascade (e.g. `.app .fuaran-button-primary:hover { background: <consumer-value>; }`).

The pre-12.N opinionated `filter: brightness(0.92)` is gone – consumers who explicitly want it back can re-add `filter: brightness(0.92)` to their own button rules without breaking the variable contract.

No data migration; no API contract to roll back.

## See also

- [`Fuaran/HOST-STYLING-CHECKLIST.md`](../../HOST-STYLING-CHECKLIST.md) §1.6 – the canonical interaction-token contract (Phase 12.N output).
- [`Fuaran/docs/THEME-BRIDGE-GUIDE.md`](../THEME-BRIDGE-GUIDE.md) – pre-12.N bridging guide for the static palette; the four worked examples above extend it to the interaction layer.
- [`Fuaran/src/Fuaran.UI.Renderer/content/fuaran-reference.css`](../../src/Fuaran.UI.Renderer/content/fuaran-reference.css) – the post-12.N reference stylesheet (88 new `:root` declarations + ~30 new rules).
- [`Fuaran/src/Fuaran.UI.Tests/ThemeTests.fs`](../../src/Fuaran.UI.Tests/ThemeTests.fs) – byte-for-byte regression covering the new variables + round-trip test for the `Interaction` field.
- [Phase 12.H migration](12-H-host-styling-contract.md) – Critical-C-i (static-palette variable surface in `fuaran-reference.css`).
- [Phase 12.K migration](12-K-theme-as-api.md) – Critical-C-ii (typed `Theme` record); 12.N extends with the `Interaction` field.
- Phase 12.N – phase body.
