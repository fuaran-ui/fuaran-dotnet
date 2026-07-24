# Phase 58 migration – mobile / responsive renderer

**Shipped:** 2026-07-11
**Scope:** `Fuaran.UI/Types.fs` (typed `Theme`) + `Fuaran.UI/Defaults.fs` + `Fuaran.UI.Renderer.Core/Theme.fs` (`toCssVariables` / `toJson` / `fromJson`) + `Fuaran.UI.Renderer/content/fuaran-reference.css` + the TypeScript byte-copy (`fuaran-ts/packages/renderer/css/fuaran.css`) + `Fuaran.UI.Tests/ThemeTests.fs`.
**Stability impact:** **Additive `Theme` field (minor bump).** `Theme` gains a required `Breakpoints` record. The only full `Theme` record literals in the tree are `Defaults.theme` and the test's `exoticTheme` (the three sample themes use `{ Defaults.theme with … }` and inherit the field for free) – so no consumer literal breaks in practice, but a consumer that hand-builds a complete `Theme` from scratch must add the `Breakpoints` field. **Responsive CSS defaults change rendered output at narrow widths** (see §4).

## What changes

### 1. `Theme.Breakpoints` (additive record field)

`Fuaran.UI/Types.fs`:

```fsharp
and Breakpoints =
    { Sm: string   // "640px"
      Md: string   // "768px"
      Lg: string } // "1024px"

and Theme =
    { …
      Segmented: Segmented
      Breakpoints: Breakpoints } // NEW
```

Values are CSS dimension strings (same convention as `Spacing` / `Radius`). `Defaults.theme.Breakpoints` is `{ Sm = "640px"; Md = "768px"; Lg = "1024px" }`.

### 2. CSS-variable emission (`Theme.toCssVariables`)

Three new tokens, emitted into the `:root` block the renderer maintains:

```
--fuaran-breakpoint-sm: 640px;
--fuaran-breakpoint-md: 768px;
--fuaran-breakpoint-lg: 1024px;
```

The static-surface variable count therefore moves **146 → 149**. These tokens exist so consumer JS and CSS container queries can read the same thresholds the renderer collapses at.

### 3. JSON round-trip (`Theme.toJson` / `fromJson`)

The flat-JSON wire shape gains a `"breakpoints": { "sm": …, "md": …, "lg": … }` object. Round-trips through `toJson |> fromJson` with structural equality (covered by the exotic-theme test).

### 4. Responsive layout collapse (reference CSS)

New `@media` rules at the **foot** of `fuaran-reference.css` (appended so they win by source order at equal specificity):

- **`≤ 768px` (md):** `.fuaran-layout-grid` → `repeat(2, 1fr)`.
- **`≤ 640px` (sm):**
  - `.fuaran-stack-horizontal` + `.fuaran-layout-split-panel` → single vertical column.
  - `.fuaran-layout-grid` → `1fr` (single column).
  - `.fuaran-tabs-bar` → horizontal scroller (`overflow-x: auto`, `flex-wrap: nowrap`); tabs no longer clip off-screen.
  - `.fuaran-kind-grid` / `.fuaran-kind-table` (the outer node wrapper) → `overflow-x: auto`; wide tabular data scrolls within its box instead of forcing page scroll.
  - Touch-target floor (WCAG 2.5.5): `.fuaran-button`, `.fuaran-form-input`, `.fuaran-select-control`, `.fuaran-file-upload`, `.fuaran-tab` → `min-height: 44px`.

**Behavioural change to flag:** a tree rendered below 768/640px now collapses its grids/stacks and scrolls its tables where it previously overflowed. Desktop output (> 768px) is byte-identical to pre-58.

### Two caveats worth knowing

1. **`@media` conditions cannot reference a custom property** – `@media (max-width: var(--fuaran-breakpoint-sm))` is invalid CSS. The media-query thresholds therefore repeat the px values *literally*. The typed `Theme.Breakpoints` record (mirrored into the `:root` tokens) is the source of truth those literals are kept in sync with, exactly like the existing `Defaults.theme` ↔ reference-CSS byte-mirror discipline. **Change both together.** A consumer who overrides `Theme.Breakpoints` to non-default values also gets new `--fuaran-breakpoint-*` tokens, but the packaged reference CSS's media queries stay at 640/768 – a consumer wanting different *collapse* points must ship their own media rules (the tokens make that a one-liner).
2. **The grid collapse uses `!important`.** `grid-template-columns` is emitted as an inline style by `Render.fs` (column count rides inline so hosts needn't safelist every N – see [Phase 67](67-grid-template-columns.md)). A media-query rule cannot override an inline style without `!important`, so the two grid-collapse rules carry it. This is the one sanctioned `!important` in the reference stylesheet, scoped to the narrow-viewport media queries.

## Feliz-parity expectation (§4l down-shift)

The responsive behaviour is **pure reference-CSS** – no renderer-emission change, no new class hooks on the emitted tree. A consumer who has down-shifted off `Fuaran.UI.Renderer` and re-implemented against the class vocabulary keeps the same class names; they inherit the responsive rules by re-copying `fuaran-reference.css` (or re-implement the six media-query rules above against the identical hooks). The typed-tree wire format is unchanged, so `fuaran-ts` / `fuaran-py` / `fuaran-go` hosts need no codec update – only the shared reference CSS gains the rules (the TS tier's byte-copy is updated in this same change).

## Verification

- `dotnet run --project src/Fuaran.UI.Tests/Fuaran.UI.Tests.fsproj` – the `Theme as API` list (byte-mirror + round-trip + count = 149) is green.
- Catalog `?viewport=mobile` boot mode + the `snapshot/viewport-mobile.spec.mts` Playwright spec assert no horizontal overflow and no clipped touch targets at the `sm` breakpoint across the NodeKind matrix.
