# Fuaran.UI.Renderer — packaged content

This folder is packed into the `Fuaran.UI.Renderer` NuGet under `content/` so consumers can locate the reference stylesheet without cloning the Fuaran repo.

## Files

- `fuaran-reference.css` — canonical reference stylesheet. Apache 2.0 licensed. Drop it into any host that wants the Fuaran reference styling unmodified, or copy + customise. Re-binding `--fuaran-*` variables at the `:root` layer is the preferred override path; replacing the file wholesale is supported for §4l down-shift portability.
- `fuaran-reference-tables.js` — the reference table-sort enhancement. Apache 2.0 licensed. Dependency-free, ES5, no build step: serve it as a file and every server-rendered `.fuaran-table` on the page gains sortable column headers (ascending → descending → the authored order; `aria-sort` mirrors the state; annotated figures parse numerically; the `–` unmeasured placeholder sorts last in both directions). Progressive enhancement — without it the tables are simply static. The file's own header carries the CSP guidance (`script-src 'self'` covers it as a file; hash the exact bytes if inlined).
- `fuaran-image-expand.js` — the reference expandable-image enhancement (Phase 1079). Apache 2.0 licensed. Dependency-free, ES5, no build step: serve it as a file and every `expandable` image on the page opens in an in-page overlay meeting the `Modal` accessibility contract (`role="dialog"` + `aria-modal`, focus trap, `Escape`, backdrop dismissal, focus restored to the thumbnail). It is a REFINEMENT, not the affordance: the renderers emit a real `<a href>` to the full-size asset, so a reader with no JavaScript reaches the picture anyway. The file's own header carries the CSP guidance and the composition rules for `caption` / `srcSet`.
- `fuaran-bridge.css.template` — copy-and-customise alias template for hosts that already own design tokens (`--color-brand`, shadcn `--primary`, MUI `--mui-palette-primary-main`, etc.). See `Fuaran/docs/THEME-BRIDGE-GUIDE.md` for the four worked examples this template is derived from.
- `README.md` — this file.

## How to use the reference stylesheet

The most common path: copy `fuaran-reference.css` into your host's static assets (e.g. `src/Client/public/fuaran-reference.css`) and reference it from your HTML `<head>`:

```html
<link rel="stylesheet" href="/fuaran-reference.css" />
```

Bundlers (Vite, webpack, esbuild) can also `@import` it from your own stylesheet:

```css
@import "../path/to/fuaran-reference.css";
```

If your host already has design tokens, layer a bridge file on top per `THEME-BRIDGE-GUIDE.md`:

```html
<link rel="stylesheet" href="/fuaran-reference.css" />
<link rel="stylesheet" href="/fuaran-bridge.css" />
```

The bridge file re-binds `--fuaran-tone-*` variables to your host's existing tokens — Fuaran then picks up your design system without forking the reference CSS.

## How to use the table-sort enhancement

Copy `fuaran-reference-tables.js` into your host's static assets alongside the stylesheet and load it as a file — `defer`, or at the end of `<body>`:

```html
<link rel="stylesheet" href="/fuaran-reference.css" />
<script src="/fuaran-reference-tables.js" defer></script>
```

That is the whole integration. The stylesheet already carries the indicator affordances (`.fuaran-table-header[data-sortable]` / `[aria-sort]`), and every attribute they key off is set by the script — so a host that ships the CSS without the script shows no sort affordance at all, which is the correct behaviour rather than a broken one.

## How to use the expandable-image enhancement

Same shape, and the same one-line integration:

```html
<link rel="stylesheet" href="/fuaran-reference.css" />
<script src="/fuaran-image-expand.js" defer></script>
```

A host that ships the CSS without the script still serves working expandable images — the anchor the renderers emit is an ordinary link to the asset, and the browser's own viewer is the fallback. The script changes where the picture opens, never whether it opens.

## Why the reference CSS lives in the NuGet

Pre-12.H, Fuaran's reference stylesheet existed only at `samples/demo/index.css` — packaged nowhere consumers could `@import` from. An integration audit (2026-05-26) found that consumers who skipped hand-copying the demo CSS ended up with ~100% styling loss inside the Fuaran-rendered region (the renderer's CSS-variable-only emission resolves to `var(--missing, /* nothing */)` which produces empty rules). Packaging the CSS as `Content` makes "use the reference styling" a one-step `<link>`, identical to the `HOST-INTEGRATION-CHECKLIST.md` (`Fuaran/`) wires which became mandatory in Phase 12.Y.2.

## Contract

The classes the stylesheet hooks against are enumerated in `Fuaran/HOST-STYLING-CHECKLIST.md`. The renderer (`Fuaran.UI.Renderer/Render.fs`) is the canonical emission source; the checklist is the canonical consumer contract; this CSS is the canonical reference implementation. All three are co-evolved per the phase that touches them.

## Licence

Apache 2.0. Copyright (c) Diametrical Ltd. See the header in `fuaran-reference.css`. The licence covers the stylesheet itself; the surrounding `Fuaran.UI.Renderer` library is also Apache 2.0 per the Fuaran repo `LICENSE` file. The CSS being Apache 2.0 explicitly supports the §4l down-shift portability promise: a consumer retiring the typed library can keep using the class vocabulary indefinitely.
