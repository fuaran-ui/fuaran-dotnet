# Phase 12.K – Fuaran theme-as-API

**Phase:** `Fuaran/roadmap/phases/12-K-fern-theme-as-api.md`
**Date:** 2026-05-26
**Stability impact:** Additive – pre-1.0 minor add per [`STABILITY.md`](../../STABILITY.md). No breaking change to the `Fuaran.UI` smart-constructor surface; new types are appended to `Fuaran.UI/Types.fs`; the renderer's pre-12.K entry point (`Render.renderWithSources`) is unchanged.

## What changes

Phase 12.K promotes Fuaran's CSS-variable bundle from a convention (consumer stylesheet) into a typed `Theme` record consumers compose and pass to the renderer. Pre-12.K, every Fuaran deployment overrode `--fuaran-*` variables at its app shell layer – the shape was documented in [`HOST-STYLING-CHECKLIST.md`](../../HOST-STYLING-CHECKLIST.md) §1 but not enforced by the type system. Post-12.K, apps build a `Theme` value the same way they build `Node` trees, Portal-emitted apps get themable shapes by construction, and the eval suite (Phase 12.E, when shipped) can assert visual output against a known theme.

Closes **Critical-C-ii** of the pilot-app-audit follow-on (the dimension-scale axes – `Spacing` / `FontScale` / `FontWeight` / `LineHeight` / `ButtonSize`) alongside [Phase 12.H](12-H-host-styling-contract.md)'s Critical-C-i (which lifted the reference CSS's hardcoded pixels into the matching `--fuaran-*` variables). The audited gap where a compact-chip button couldn't be expressed without monkey-patching `.fuaran-button` is closed by the typed `ButtonSize` axis.

Six artefacts land:

1. **`Theme` + `ColorVar` + `ToneStops` + `Tones` + `Spacing` + `FontScale` + `FontWeight` + `LineHeight` + `Radius` + `ButtonSize`** records / DUs appended to [`src/Fuaran.UI/Types.fs`](../../src/Fuaran.UI/Types.fs).
2. **`Defaults.theme`** in [`src/Fuaran.UI/Defaults.fs`](../../src/Fuaran.UI/Defaults.fs) – byte-for-byte mirror of the post-12.H `fuaran-reference.css` `:root` block. A regression test in `Fuaran.UI.Tests/ThemeTests.fs` reads the reference CSS at runtime and asserts every `--fuaran-X: value;` declaration in the reference appears in `Theme.toCss Defaults.theme` with the same value, and vice versa.
3. **`Theme.toCss` + `Theme.toCssVariables` + `Theme.colorVarToCss`** in [`src/Fuaran.UI.Renderer/Theme.fs`](../../src/Fuaran.UI.Renderer/Theme.fs) – pure transforms from `Theme` to the `:root { ... }` block (and to the underlying `(name, value)` pair list, exposed for testability).
4. **`Theme.toJson` / `Theme.fromJson`** in the same file – flat-JSON wire shape (`{"tones": {"default": {"background": {"kind": "hex", "value": "#ffffff"}, ...}, ...}, "spacing": {...}, ...}`). `ColorVar` cases are tagged objects (`{"kind":"hex","value":"#1d4ed8"}` / `{"kind":"oklch","l":...,"c":...,"h":...,"alpha":...}` / `{"kind":"cssRaw","value":"..."}`). Uses Fable.SimpleJson (already centrally pinned), so the round-trip works on both .NET and Fable.
5. **`Render.themeStyleElement` + `Render.renderWithTheme`** in [`src/Fuaran.UI.Renderer/Render.fs`](../../src/Fuaran.UI.Renderer/Render.fs) – new entry point that mounts a `<style>` element carrying the theme's CSS bundle alongside the rendered node tree. The pre-12.K `Render.renderWithSources` entry point is unchanged (no auto-mounted theme = current consumer-supplied-CSS path).
6. **Sample themes** under [`samples/themes/`](../../samples/themes/) – `Default.fs` (mirrors `Defaults.theme`), `Dark.fs` (dark-mode variant), `HighContrast.fs` (WCAG-AAA accessibility variant). Compile-checked via the test project; each is a single F# value of type `Theme` doubling as documentation for the `ColorVar` DSL.

## Diff highlights

### New types – `Fuaran.UI/Types.fs`

```fsharp
[<RequireQualifiedAccess>]
type ColorVar =
    | Hex of string
    | OKLCH of l: float * c: float * h: float * alpha: float
    | CssRaw of string

type ToneStops =
    { Background: ColorVar; Foreground: ColorVar; Border: ColorVar }

type Tones =
    { Default: ToneStops; Subdued: ToneStops; Brand: ToneStops
      Success: ToneStops; Warning: ToneStops; Critical: ToneStops; Info: ToneStops }

type Spacing = { Xs: string; Sm: string; Md: string; Lg: string; Xl: string }
type FontScale = { Xs: string; Sm: string; Base: string; Lg: string; Xl: string; XXl: string; XXXl: string }
type FontWeight = { Regular: int; Medium: int; Semibold: int; Bold: int }
type LineHeight = { Tight: float; Normal: float; Relaxed: float }
type Radius = { Sm: string; Md: string; Lg: string; Full: string }
type ButtonSize = { PadY: string; PadX: string; FontSize: string }

type Theme =
    { Tones: Tones
      Spacing: Spacing
      FontScale: FontScale
      FontWeight: FontWeight
      LineHeight: LineHeight
      Radius: Radius
      ButtonSize: ButtonSize
      BorderWidth: string }
```

### New value – `Fuaran.UI/Defaults.fs`

```fsharp
let theme: Theme = { Tones = tones; Spacing = spacing; ... ; BorderWidth = "1px" }
```

Every leaf value mirrors the matching `--fuaran-*` declaration in `fuaran-reference.css`. The byte-for-byte regression test pins this – a stray edit to either side fails immediately.

### New renderer entry point – `Fuaran.UI.Renderer/Render.fs`

```fsharp
let themeStyleElement (theme: Theme) : ReactElement =
    Html.style [ prop.dangerouslySetInnerHTML (Theme.toCss theme) ]

let renderWithTheme (theme: Theme) (sources: BindingSources)
                    (dispatch: 'Msg -> unit) (node: Node<'Msg>) : ReactElement =
    React.fragment [ themeStyleElement theme; renderWithSources sources dispatch node ]
```

The pre-12.K `renderWithSources` is unchanged – apps that wired `fuaran-reference.css` separately keep working without code edits.

## Design deviation from the phase body

The phase body's task #1 sketched a `Theme` record with `Brand: ColorVar`, `Sidebar: ColorVar`, `Surface: { Background; Foreground; Subtle }`, `Tones { Info; Success; Warning; Danger; Neutral }`, `Typography { FontSans; FontMono; ScaleBaseRem }`, `Radius { Small; Medium; Large }`, and `Shadow { Small; Medium; Large }`. **The implemented record does not match this sketch.** The deviation is deliberate – the sketch was authored before [Phase 12.H](12-H-host-styling-contract.md) finalised the reference-CSS variable surface, and shipping the sketch verbatim would have produced a `Theme` record that does not byte-for-byte project to the post-12.H reference CSS (the regression test's hardest constraint).

Concrete mismatches:

| Sketch | Reference CSS | 12.K shape |
|---|---|---|
| `Brand: ColorVar` (top-level) | `--fuaran-tone-brand-{bg,fg,border}` (3-stop tone) | `Tones.Brand: ToneStops` |
| `Sidebar: ColorVar` | no `--fuaran-tone-sidebar-*` vars | _not modelled_ – consumers add a sibling var bundle (per [`THEME-BRIDGE-GUIDE.md`](../THEME-BRIDGE-GUIDE.md)) |
| `Surface: { Background; Foreground; Subtle }` | covered by `--fuaran-tone-default-{bg,fg,border}` | `Tones.Default: ToneStops` |
| `Tones { Info; Success; Warning; Danger; Neutral }` (5 tones) | 7 tones (`default`, `subdued`, `brand`, `success`, `warning`, `critical`, `info`) | `Tones` with all 7 |
| `Typography { FontSans; FontMono; ScaleBaseRem }` | discrete `--fuaran-text-{xs..3xl}` sizes | `FontScale` (7 discrete sizes) + `FontWeight` + `LineHeight` per Critical-C-ii |
| `Radius { Small; Medium; Large }` (3 sizes) | `--fuaran-radius-{sm,md,lg,full}` (4 sizes) | `Radius { Sm; Md; Lg; Full }` |
| `Shadow { Small; Medium; Large }` | no `--fuaran-shadow-*` vars | _not modelled_ – per the phase body's anti-pattern: "Don't bake font + radius + shadow into v1 if it adds cost without compound value. They're in the proposed surface above because the current CSS-variable bundle covers them, but if shipping them in v1 risks scope creep, defer to 12.K.2." Shadow has no CSS-bundle coverage today, so it defers cleanly. |

The Critical-C-ii task in the phase body (dimension-scale records) was authored post-12.H and matches the reference-CSS surface – it shipped verbatim.

If the sketch's `Sidebar` / `Shadow` axes become desirable later, they extend the `Theme` record additively (pre-1.0 minor add) and ship alongside matching reference-CSS variables.

## Consumer adoption

### Pre-12.K shape (still works)

```fsharp
// App boot:
// 1. Ship fuaran-reference.css (or a bridge stylesheet) as a `<link>` /
//    `<style>` tag at the app shell.
// 2. Render via the pre-12.K entry point — no theme parameter.

Render.renderWithSources sources dispatch myRootNode
```

### 12.K shape with `Defaults.theme` (visual-identity preserving)

```fsharp
// App boot:
// 1. Remove the `<link rel="stylesheet" href="fuaran-reference.css">` from
//    the HTML host (or keep it — they're equivalent byte-for-byte).
// 2. Render via the new entry point with Defaults.theme.

Render.renderWithTheme Defaults.theme sources dispatch myRootNode
```

The renderer emits the same CSS-variable bundle the reference stylesheet did; no visual change.

### 12.K shape with a custom theme

```fsharp
let myTheme : Theme =
    { Defaults.theme with
        Tones =
            { Defaults.theme.Tones with
                Brand =
                    { Defaults.theme.Tones.Brand with
                        Foreground = ColorVar.Hex "#0d47a1" } } }

Render.renderWithTheme myTheme sources dispatch myRootNode
```

### Cookbook adoption

Cookbook recipes that ship a theme switch to the typed surface:

```fsharp
// Recipe step (excerpt):

open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Renderer

let themeForCook : Theme =
    { Defaults.theme with
        Tones =
            { Defaults.theme.Tones with
                Brand =
                    { Background = ColorVar.Hex "#{{brand-bg}}"
                      Foreground = ColorVar.Hex "#{{brand-fg}}"
                      Border = ColorVar.Hex "#{{brand-border}}" } } }

let view (model: Model) (dispatch: Msg -> unit) =
    Render.renderWithTheme themeForCook (buildSources model) dispatch (root model)
```

The Catalog phase (12.S) loads sample themes via the typed `Theme` record rather than raw CSS-variable bundles – this is what unlocks the theme-switcher UI in the catalog.

## Stability classification

Per [`STABILITY.md`](../../STABILITY.md):

- `Theme`, `ColorVar`, `ToneStops`, `Tones`, `Spacing`, `FontScale`, `FontWeight`, `LineHeight`, `Radius`, `ButtonSize` join the §4b record contract for `Fuaran.UI`.
- `Defaults.theme` joins the `Defaults.X` field set for `Fuaran.UI`.
- `Theme.toCss`, `Theme.toJson`, `Theme.fromJson`, `Theme.toCssVariables`, `Theme.colorVarToCss` join the `Fuaran.UI.Renderer.Theme` public surface.
- `Render.themeStyleElement`, `Render.renderWithTheme` join the `Fuaran.UI.Renderer` public surface.

All additions. Pre-1.0 means consumers should still pin to the exact patch. The renderer's pre-12.K signature (`Render.renderWithSources`) is unchanged.

## Rollback

If `renderWithTheme` surfaces an issue (incorrect CSS-variable emission, theme-switching reflow regression, etc.) after a consumer migration:

1. Revert the consumer code from `Render.renderWithTheme theme sources dispatch node` to `Render.renderWithSources sources dispatch node`.
2. Restore the `<link rel="stylesheet" href="fuaran-reference.css">` (or bridge stylesheet) at the HTML host.

Pre-12.K behaviour returns immediately – `renderWithSources` and `Defaults.theme` / `fuaran-reference.css` are mutually substitutable. No data migration; no API contract to roll back.

## See also

- [`Fuaran/HOST-STYLING-CHECKLIST.md`](../../HOST-STYLING-CHECKLIST.md) – the canonical CSS variable + class-hook contract (Phase 12.H output).
- [`Fuaran/docs/THEME-BRIDGE-GUIDE.md`](../THEME-BRIDGE-GUIDE.md) – four worked examples for bridging consumer design tokens to the `--fuaran-*` surface.
- [`Fuaran/src/Fuaran.UI.Renderer/content/fuaran-reference.css`](../../src/Fuaran.UI.Renderer/content/fuaran-reference.css) – the post-12.H reference stylesheet (the byte-for-byte regression target).
- [`Fuaran/samples/themes/`](../../samples/themes/) – Default / Dark / HighContrast sample themes.
- [`Fuaran/src/Fuaran.UI.Tests/ThemeTests.fs`](../../src/Fuaran.UI.Tests/ThemeTests.fs) – byte-for-byte regression + round-trip tests.
- [Phase 12.H migration](12-H-host-styling-contract.md) – Critical-C-i (variable surface lift in `fuaran-reference.css`).
- Phase 12.K – phase body (Critical-C-ii dimension-scale records).
